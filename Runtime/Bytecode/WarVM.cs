#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Audit;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;
using FixMath;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Stack-based bytecode virtual machine for WarScript.
    /// Replaces tree-walk execution with a tight dispatch loop.
    /// </summary>
    public class WarVM
    {
        private const int StackMax = BytecodeVerifier.StackLimit; // 1,024: the verifier bounds loaded functions by it
        private const int FramesMax = 128;
        private const int HandlersMax = 32;

        private readonly WarValue[] _stack = new WarValue[StackMax];
        private int _sp;

        private readonly CallFrame[] _frames = new CallFrame[FramesMax];
        private int _frameCount;

        private readonly TryHandler[] _handlers = new TryHandler[HandlersMax];
        private int _handlerCount;

        private readonly WarScriptLanguage _script;

        /// <summary>
        /// Captures the return value from the outermost frame.
        /// Set by DoReturn when _frameCount hits 0.
        /// </summary>
        private WarValue _topLevelResult;

        // Ensure blocks entered with something still to do when they finish:
        // an exception to re-raise (an ensure-only handler caught it) or a
        // value to return (a return left the begin body). Innermost last.
        // Ensure blocks can nest (one inside another, or in a function called
        // from one), so this is a stack. A record ends when its ensure block
        // completes, or is discarded when control leaves the block another
        // way: a return from its frame, an error caught outside it, or its
        // frame being unwound (CRASH-7).
        private PendingEnsure[] _pending = new PendingEnsure[8];
        private int _pendingCount;

        // Scope depth tracking: PushScope increments, PopScope decrements.
        // TryHandler saves it so DoHandleException can restore on unwind.
        private int _scopeDepth;

        // Instruction and memory budgets: shared by every VM of the instance
        // and reset only when an outermost host entry starts (T1.5, PROP-2).
        private readonly ExecutionBudget _budget;

        // True while Execute() runs. A busy VM is never reset: a host entry
        // that arrives meanwhile (a native calling back into the instance)
        // runs on another VM (T1.5, DEC-16).
        private bool _running;

        // ── Debugger state ──
        // Only accessed when _script.DebugHook != null (zero cost otherwise).
        private StepMode _stepMode;
        private int _stepFrameDepth;
        private int _lastDebugLine;

        // ── Coroutine support ──
        // When the VM is used as a coroutine executor, yield opcodes
        // set these fields and return from Execute(). The coroutine
        // wrapper reads them to determine how to wait.
        private bool _yielded;
        private YieldType _yieldType;
        private F64 _yieldWaitDuration;

        /// <summary>True if the last Execute() stopped because of a yield opcode.</summary>
        public bool IsYielded => _yielded;

        /// <summary>The type of yield (NextTick, Wait, Until).</summary>
        public YieldType SuspendedYieldType => _yieldType;

        /// <summary>For YieldWait: the requested wait duration in seconds.</summary>
        public F64 SuspendedWaitDuration => _yieldWaitDuration;

        /// <summary>True if the VM has finished executing (no more frames).</summary>
        public bool IsCompleted => _frameCount == 0 && !_yielded;

        /// <summary>
        /// No frames, empty value stack, no installed handlers and nothing
        /// pending. Checked by the test invariants after every outermost
        /// entry that did not yield.
        /// </summary>
        internal bool IsIdle => _frameCount == 0 && _sp == 0 && _handlerCount == 0
                                && _pendingCount == 0 && !_yielded;

        /// <summary>True while the VM is executing (see <c>_running</c>).</summary>
        internal bool IsRunning => _running;

        private struct TryHandler
        {
            public int RescueIP;
            public int EnsureIP;
            public int EndIP;
            public int FrameIndex;
            public int SavedSP;
            public bool HasRescue;
            public int SavedScopeDepth;
        }

        private struct PendingEnsure
        {
            /// <summary>True: return <see cref="Value"/>; false: re-raise it.</summary>
            public bool IsReturn;
            public WarValue Value;
            public int FrameIndex;
            /// <summary>End of the ensure block (exclusive).</summary>
            public int EndIP;
            /// <summary>
            /// Handler count when the ensure block was entered. Handlers below
            /// it enclose the block; an error caught by one of them leaves it.
            /// </summary>
            public int HandlerDepth;
        }

        // Context depths when the current entry (Run, RunFunction,
        // InitCoroutine, ResumeCoroutine) started. A host fault restores them.
        private int _entryMemoryDepth;
        private int _entryDefinitionDepth;
        private int _entryInstanceDepth;

        public WarVM(WarScriptLanguage script)
        {
            _script = script;
            _interner = script.Strings;
            _budget = script.Budget;
        }

        private readonly StringInterner _interner;

        /// <summary>
        /// Reset all VM state for reuse. Avoids re-allocating the stack,
        /// frame, and handler arrays on every Call() invocation. The budgets
        /// belong to the instance and are not touched (T1.5).
        /// </summary>
        private void Reset()
        {
            if (_running)
                throw new System.InvalidOperationException("WarVM reset while it is executing");
            _sp = 0;
            _frameCount = 0;
            _handlerCount = 0;
            _topLevelResult = default;
            ClearPending(0);
            _scopeDepth = 0;
            _yielded = false;
            _yieldType = YieldType.NextTick;
            _yieldWaitDuration = F64.Zero;
            _stepMode = StepMode.Continue;
            _stepFrameDepth = 0;
            _lastDebugLine = -1;
        }

        public void Run(CompiledFunction main)
        {
            Reset();
            RecordEntryDepths();
            if (!StackFits(0, 0, main)) return;

            _frames[0] = new CallFrame
            {
                Function = main,
                IP = 0,
                StackBase = 0,
                SavedScopeDepth = _scopeDepth
            };
            _frameCount = 1;

            Execute();
        }

        /// <summary>
        /// Run a compiled function with pre-supplied arguments.
        /// Used by the Call() API for host→script invocations (tick loops, events).
        /// </summary>
        public WarValue RunFunction(CompiledFunction func, WarValue[] arguments)
        {
            Reset();
            RecordEntryDepths();
            if (!StackFits(0, 0, func)) return _topLevelResult;

            for (int i = 0; i < func.Arity; i++)
                _stack[_sp++] = i < arguments.Length ? arguments[i] : WarValue.Null;

            _frames[0] = new CallFrame
            {
                Function = func,
                IP = 0,
                StackBase = 0,
                SavedScopeDepth = _scopeDepth
            };
            _frameCount = 1;

            Execute();

            return _topLevelResult;
        }

        /// <summary>
        /// Set up a coroutine: push arguments, create the initial frame,
        /// and execute until the first yield or completion.
        /// The VM preserves its full state between calls — do NOT call Reset().
        /// </summary>
        public void InitCoroutine(CompiledFunction func, WarValue[] arguments)
        {
            Reset();
            RecordEntryDepths();
            if (!StackFits(0, 0, func)) return;

            for (int i = 0; i < func.Arity; i++)
                _stack[_sp++] = i < arguments.Length ? arguments[i] : WarValue.Null;

            _frames[0] = new CallFrame
            {
                Function = func,
                IP = 0,
                StackBase = 0,
                SavedScopeDepth = _scopeDepth
            };
            _frameCount = 1;

            Execute();
        }

        /// <summary>
        /// Resume a yielded coroutine. Continues execution from where the
        /// last yield opcode stopped. Does NOT reset VM state.
        /// </summary>
        public void ResumeCoroutine()
        {
            RecordEntryDepths();
            _yielded = false;
            _yieldType = YieldType.NextTick;
            _yieldWaitDuration = F64.Zero;
            Execute();
        }

        // ────────────────────────────────────────────────────────
        //  Main dispatch loop
        // ────────────────────────────────────────────────────────

        private void RecordEntryDepths()
        {
            _entryMemoryDepth = _script.MemoryContext.Depth;
            _entryDefinitionDepth = _script.DefinitionContext.Depth;
            _entryInstanceDepth = _script.ClassInstanceContext.Depth;
        }

        /// <summary>
        /// Runs the dispatch loop: the VM's exception boundary (plan T1.3).
        /// <list type="bullet">
        /// <item>A <see cref="ScriptErrorException"/> (thrown by runtime code
        /// on the VM's behalf) becomes a script error that <c>begin/rescue</c>
        /// can catch. The push helper itself is branch-free (hot path): every
        /// frame push checks the callee's verified <c>MaxStack</c> against
        /// the room left (T1.7), so no push runs past the value stack.</item>
        /// <item>An error no handler catches unwinds every frame, popping the
        /// scopes and class contexts they pushed, and stays raised for the
        /// caller (an enclosing VM or the host entry point).</item>
        /// <item>Any other exception is a host fault: a VM bug or a failure of
        /// the host. The context stacks go back to their depths at the start
        /// of this entry, the VM is reset, and the exception propagates — out
        /// of every enclosing VM — to the host entry point, which reports it
        /// and stops. A <see cref="SyntaxException"/> from an imported file
        /// is cleaned up the same way and reaches the host unchanged.</item>
        /// </list>
        /// </summary>
        private void Execute()
        {
            if (_running)
                throw new System.InvalidOperationException("WarVM entered while it is executing");
            _running = true;
            try { ExecuteGuarded(); }
            finally { _running = false; }
        }

        private void ExecuteGuarded()
        {
            while (true)
            {
                try
                {
                    ExecuteLoop();
                    break;
                }
                catch (ScriptErrorException e)
                {
                    _script.ExceptionContext.RaiseException(e.Message, e.Kind);
                    if (!HandleCaughtError()) break;
                }
                catch
                {
                    // Also a SyntaxException from compiling an import: it
                    // propagates to the host as in v1 (imports become
                    // compile-time in Phase 3), after the same cleanup.
                    _running = false; // so the cleanup may reset the VM
                    AbortAfterHostFault();
                    throw;
                }
            }

            // Frames left behind without a yield mean an unhandled error.
            if (_frameCount > 0 && !_yielded)
                UnwindAll();
        }

        private bool HandleCaughtError()
        {
            if (_frameCount == 0) return false;
            int fi = _frameCount - 1;
            var code = _frames[fi].Function.Chunk.Code;
            var constants = _frames[fi].Function.Chunk.Constants;
            return DoHandleException(ref fi, ref code, ref constants);
        }

        /// <summary>
        /// Pops every frame together with what it pushed on the context
        /// stacks, after an error that no handler caught. The error itself
        /// stays raised.
        /// </summary>
        private void UnwindAll()
        {
            while (_frameCount > 0)
                PopFrame(_frameCount - 1);
            _sp = 0;
            _handlerCount = 0;
            ClearPending(0);
            _scopeDepth = 0;
        }

        /// <summary>
        /// Puts the context stacks back where they were when this entry
        /// started and resets the VM, after a C# exception escaped the
        /// dispatch loop. The VM's own bookkeeping cannot be trusted at that
        /// point (the exception may have hit half way through an instruction),
        /// so this goes by the recorded depths, not by the frames.
        /// </summary>
        private void AbortAfterHostFault()
        {
            while (_script.MemoryContext.Depth > _entryMemoryDepth)
                _script.MemoryContext.EndScope();
            while (_script.DefinitionContext.Depth > _entryDefinitionDepth)
                _script.DefinitionContext.EndScope();
            while (_script.ClassInstanceContext.Depth > _entryInstanceDepth)
                _script.ClassInstanceContext.PopValue();
            if (_script.ExceptionContext.IsRaised())
                _script.ExceptionContext.RescueException();
            Reset();
        }

        private void ExecuteLoop()
        {
            // These are reloaded from the _frames array whenever the current
            // frame changes (after Call or Return).
            int fi = _frameCount - 1;
            List<byte> code = _frames[fi].Function.Chunk.Code;
            List<WarValue> constants = _frames[fi].Function.Chunk.Constants;
            var budget = _budget;

            while (true)
            {
                // ── Instruction budget (shared by the instance's VMs, T1.5) ──
                if (--budget.InstructionsLeft < 0)
                {
                    // The budget stays exhausted until the next outermost
                    // entry (BUDGET-2, Q-11): every further instruction raises
                    // again, so rescue and ensure bodies cannot run and the
                    // error ends the entry whatever the script rescues.
                    budget.InstructionsLeft = -1;
                    RuntimeError("Instruction budget exceeded");
                    if (DoHandleException(ref fi, ref code, ref constants))
                        continue;
                    return;
                }

                // ── Source-map debugger ──
                // Cost when disabled: one null check per instruction (branch-predicted away).
                // When enabled: fires only on source-line changes.
                if (_script.DebugHook != null)
                {
                    var ip = _frames[fi].IP;
                    if (ip < code.Count)
                    {
                        var currentLine = _frames[fi].Function.Chunk.Lines.GetLine(ip);
                        if (currentLine != _lastDebugLine && currentLine > 0)
                        {
                            var shouldBreak = false;
                            switch (_stepMode)
                            {
                                case StepMode.Continue:
                                    shouldBreak = _script.HasBreakpoint(currentLine);
                                    break;
                                case StepMode.StepInto:
                                    shouldBreak = true;
                                    break;
                                case StepMode.StepOver:
                                    shouldBreak = _frameCount <= _stepFrameDepth;
                                    break;
                                case StepMode.StepOut:
                                    shouldBreak = _frameCount < _stepFrameDepth;
                                    break;
                            }

                            if (shouldBreak)
                            {
                                _lastDebugLine = currentLine;
                                var ctx = BuildDebugContext(fi, currentLine);
                                _script.DebugHook(ctx);
                                _stepMode = ctx.Action;
                                _stepFrameDepth = _frameCount;
                            }
                            else
                            {
                                _lastDebugLine = currentLine;
                            }
                        }
                    }
                }

                var instruction = (OpCode)code[_frames[fi].IP++];

                switch (instruction)
                {
                    // ══════════════════════════════════════════════
                    //  Constants & Literals
                    // ══════════════════════════════════════════════

                    case OpCode.Constant:
                    {
                        var idx = ReadU16(code, ref _frames[fi].IP);
                        Push(constants[idx]);
                        break;
                    }
                    case OpCode.Null:  Push(WarValue.Null);  break;
                    case OpCode.True:  Push(WarValue.True);  break;
                    case OpCode.False: Push(WarValue.False); break;

                    // ══════════════════════════════════════════════
                    //  Stack management
                    // ══════════════════════════════════════════════

                    case OpCode.Pop:  _sp--; break;
                    case OpCode.PopN: _sp -= code[_frames[fi].IP++]; break;
                    case OpCode.Dup:  Push(Peek()); break;

                    // ══════════════════════════════════════════════
                    //  Scope management
                    // ══════════════════════════════════════════════

                    case OpCode.PushScope:
                        _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
                        _scopeDepth++;
                        break;
                    case OpCode.PopScope:
                        if (_script.AuditSink != null) AuditBlockEnd();
                        _script.MemoryContext.EndScope();
                        _scopeDepth--;
                        break;

                    // ══════════════════════════════════════════════
                    //  Variables
                    // ══════════════════════════════════════════════

                    case OpCode.GetLocal:
                    {
                        var slot = ReadU16(code, ref _frames[fi].IP);
                        Push(_stack[_frames[fi].StackBase + slot]);
                        break;
                    }
                    case OpCode.SetLocal:
                    {
                        var slot = ReadU16(code, ref _frames[fi].IP);
                        _stack[_frames[fi].StackBase + slot] = Peek();
                        break;
                    }
                    case OpCode.GetGlobal:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
                        var global = _script.MemoryContext.GetScope().Get(name);
                        if (_script.AuditSink != null) AuditGlobalRead(name, global.IsNull, fi);
                        Push(global);
                        break;
                    }
                    case OpCode.SetGlobal:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
                        if (_script.AuditSink != null) AuditGlobalWrite(name, fi);
                        _script.MemoryContext.GetScope().Set(name, Peek());
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Arithmetic
                    // ══════════════════════════════════════════════

                    case OpCode.Add:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                            Push(WarValue.FromNumeric(a.Numeric + b.Numeric));
                        else if (a.IsArray || b.IsArray)
                        {
                            if (a.IsArray != b.IsArray && _script.AuditSink != null)
                                Audit(AuditKind.ArrayPlusValue, fi, Describe(a) + " + " + Describe(b));
                            long length = (a.IsArray ? a.ArrayValue.Count : 1L) + (b.IsArray ? b.ArrayValue.Count : 1L);
                            if (!CheckArrayLength(length) || !TrackAlloc(EstimateArrayBytes(length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(AddArrays(a, b));
                        }
                        else
                        {
                            if (_script.AuditSink != null) AuditConcatenation(a, b, fi);
                            var left = Text(a); var right = Text(b);
                            long length = (long)left.Length + right.Length;
                            if (!CheckTextLength(length) || !TrackAlloc(EstimateTextBytes(length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromText(_interner.Intern(left + right)));
                        }
                        break;
                    }
                    case OpCode.Sub:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                            Push(WarValue.FromNumeric(a.Numeric - b.Numeric));
                        else
                        {
                            if (_script.AuditSink != null)
                                Audit(AuditKind.TextSubtraction, fi, Describe(a) + " - " + Describe(b));
                            // The result is never longer than the left operand, so
                            // there is no length check. 'text - ""' leaves the text
                            // as it is (String.Replace rejects an empty pattern; T1.4).
                            var left = Text(a); var right = Text(b);
                            var s = _interner.Intern(right.Length == 0 ? left : left.Replace(right, ""));
                            if (!TrackAlloc(EstimateTextBytes(s.Length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromText(s));
                        }
                        break;
                    }
                    case OpCode.Mul:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                            Push(WarValue.FromNumeric(a.Numeric * b.Numeric));
                        else if (a.IsText && b.IsNumeric || b.IsText && a.IsNumeric)
                        {
                            var text = a.IsText ? a.TextValue : b.TextValue;
                            var count = WarValue.ToInt(a.IsText ? b.Numeric : a.Numeric);
                            long length = count <= 0 ? 0 : (long)text.Length * count;
                            if (!CheckTextLength(length) || !TrackAlloc(EstimateTextBytes(length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromText(_interner.Intern(WarValue.RepeatString(text, count))));
                        }
                        else
                        {
                            RuntimeError("Unable to multiply non-numeric values");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.Div:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                        {
                            if (b.Numeric == F64.Zero)
                            {
                                RuntimeError("Division by zero");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromNumeric(a.Numeric / b.Numeric));
                        }
                        else
                        {
                            RuntimeError("Unable to divide non-numeric values");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.Mod:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                        {
                            if (b.Numeric == F64.Zero)
                            {
                                RuntimeError("Modulo by zero");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromNumeric(a.Numeric % b.Numeric));
                        }
                        else
                        {
                            RuntimeError("Unable to perform modulo for non-numeric values");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.Negate:
                    {
                        var v = Pop();
                        if (!v.IsNumeric && _script.AuditSink != null)
                            Audit(AuditKind.NegateNonNumeric, fi, "-" + Describe(v));
                        Push(WarValue.FromNumeric(-v.Numeric));
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Comparison
                    // ══════════════════════════════════════════════

                    case OpCode.Equal:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNull || b.IsNull)
                            Push(WarValue.FromLogical(a.IsNull && b.IsNull));
                        else if (a.Tag == b.Tag)
                        {
                            var equal = a.Equals(b);
                            if (equal && a.IsClass && _script.AuditSink != null) AuditClassEquality(a, b, "==", fi);
                            Push(WarValue.FromLogical(equal));
                        }
                        else
                        {
                            var equal = Text(a) == Text(b);
                            if (_script.AuditSink != null) AuditMixedEquality(a, b, "==", equal, fi);
                            Push(WarValue.FromLogical(equal));
                        }
                        break;
                    }
                    case OpCode.NotEqual:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNull || b.IsNull)
                            Push(WarValue.FromLogical(!(a.IsNull && b.IsNull)));
                        else if (a.Tag == b.Tag)
                        {
                            var equal = a.Equals(b);
                            if (equal && a.IsClass && _script.AuditSink != null) AuditClassEquality(a, b, "!=", fi);
                            Push(WarValue.FromLogical(!equal));
                        }
                        else
                        {
                            var equal = Text(a) == Text(b);
                            if (_script.AuditSink != null) AuditMixedEquality(a, b, "!=", !equal, fi);
                            Push(WarValue.FromLogical(!equal));
                        }
                        break;
                    }
                    case OpCode.Less:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(Compare(a, b, fi) < 0));
                        break;
                    }
                    case OpCode.LessEqual:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(Compare(a, b, fi) <= 0));
                        break;
                    }
                    case OpCode.Greater:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(Compare(a, b, fi) > 0));
                        break;
                    }
                    case OpCode.GreaterEqual:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(Compare(a, b, fi) >= 0));
                        break;
                    }

                    // ── Superinstruction: Less + JumpIfFalse + Pop ──
                    // Fuses three dispatches into one. Pops two operands,
                    // compares, and jumps if the first is not less.
                    // No intermediate boolean is pushed.

                    case OpCode.LessJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!(Compare(a, b, fi) < 0))
                            _frames[fi].IP += offset;
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Logical
                    // ══════════════════════════════════════════════

                    case OpCode.Not:
                        Push(WarValue.FromLogical(!IsTruthy(Pop())));
                        break;

                    // ══════════════════════════════════════════════
                    //  Control flow
                    // ══════════════════════════════════════════════

                    case OpCode.Jump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.JumpIfFalse:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        if (!IsTruthy(Peek()))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.JumpIfTrue:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        if (IsTruthy(Peek()))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.Loop:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        _frames[fi].IP -= offset;
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Function calls
                    // ══════════════════════════════════════════════

                    case OpCode.Call:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var funcName = constants[nameIdx].TextValue;

                        var def = _script.DefinitionContext.GetScope().GetFunction(funcName, argCount);

                        // ── Fallback: check if the name is a variable holding a function value ──
                        CompiledFunction? lambdaFunc = null;
                        if (def == null)
                        {
                            var funcVal = _script.MemoryContext.GetScope().Get(funcName);
                            if (funcVal.IsNativeObject && funcVal.Ref is CompiledFunction cf)
                                lambdaFunc = cf;
                            else
                            {
                                RuntimeError(System.FormattableString.Invariant($"Function '{funcName}' with {argCount} args is not defined"));
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                        }

                        if (lambdaFunc != null)
                        {
                            // Call a function value (lambda or function reference)
                            if (!StackFits(_sp - argCount, argCount, lambdaFunc))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var arity = lambdaFunc.Arity;
                            while (argCount < arity)
                            {
                                Push(WarValue.Null);
                                argCount++;
                            }
                            _script.MemoryContext.PushScope(
                                _script.MemoryContext.NewScope(_script.UserMemoryScope, MemoryScopeKind.FunctionLocal));
                            var newBase = _sp - argCount;
                            if (_frameCount >= FramesMax)
                            {
                                _script.MemoryContext.EndScope();
                                RuntimeError("Stack overflow");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            _frames[_frameCount] = new CallFrame
                            {
                                Function = lambdaFunc,
                                IP = 0,
                                StackBase = newBase,
                                Kind = FrameKind.Function,
                                SavedScopeDepth = _scopeDepth
                            };
                            _frameCount++;
                            fi = _frameCount - 1;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else if (def is NativeFunctionDefinition nativeFn)
                        {
                            var args = CollectArgs(argCount);
                            WarValue result;
                            try { result = nativeFn.NativeBody(args); _script.ThrowPendingHostFault(); }
                            catch (System.Exception e)
                            {
                                RaiseNativeFailure("Native function", funcName, e);
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(result);
                        }
                        else if (def!.Compiled != null)
                        {
                            // Pad missing arguments with null for default parameters.
                            // The function body's desugared null-checks will assign defaults.
                            if (!StackFits(_sp - argCount, argCount, def.Compiled))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var arity = def.Compiled.Arity;
                            while (argCount < arity)
                            {
                                Push(WarValue.Null);
                                argCount++;
                            }

                            // Push a MemoryScope for function-local variables
                            // (parented to UserMemoryScope, matching tree-walker semantics)
                            _script.MemoryContext.PushScope(
                                _script.MemoryContext.NewScope(_script.UserMemoryScope, MemoryScopeKind.FunctionLocal));

                            // Arguments are already on the stack at sp-argCount .. sp-1
                            var newBase = _sp - argCount;
                            if (_frameCount >= FramesMax)
                            {
                                _script.MemoryContext.EndScope();
                                RuntimeError("Stack overflow");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            _frames[_frameCount] = new CallFrame
                            {
                                Function = def.Compiled,
                                IP = 0,
                                StackBase = newBase,
                                Kind = FrameKind.Function,
                                SavedScopeDepth = _scopeDepth
                            };
                            _frameCount++;
                            fi = _frameCount - 1;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else
                        {
                            RuntimeError($"Function '{funcName}' is not compiled");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }

                    case OpCode.CallValue:
                    {
                        // Call a function value on the stack.
                        // Stack layout: [..., funcValue, arg0, arg1, ...]
                        var argCount = (int)code[_frames[fi].IP++];
                        var funcVal = _stack[_sp - argCount - 1];

                        if (!(funcVal.IsNativeObject && funcVal.Ref is CompiledFunction lambdaCF))
                        {
                            RuntimeError("Value is not callable");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        if (!StackFits(_sp - argCount - 1, argCount, lambdaCF))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        // Remove the function value from under the args
                        var cvArgBase = _sp - argCount - 1;
                        for (int i = 0; i < argCount; i++)
                            _stack[cvArgBase + i] = _stack[cvArgBase + i + 1];
                        _sp--;

                        // Pad missing arguments with null for default parameters
                        var cvArity = lambdaCF.Arity;
                        while (argCount < cvArity)
                        {
                            Push(WarValue.Null);
                            argCount++;
                        }

                        _script.MemoryContext.PushScope(
                            _script.MemoryContext.NewScope(_script.UserMemoryScope, MemoryScopeKind.FunctionLocal));

                        var cvNewBase = _sp - argCount;
                        if (_frameCount >= FramesMax)
                        {
                            _script.MemoryContext.EndScope();
                            RuntimeError("Stack overflow");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        _frames[_frameCount] = new CallFrame
                        {
                            Function = lambdaCF,
                            IP = 0,
                            StackBase = cvNewBase,
                            Kind = FrameKind.Function,
                            SavedScopeDepth = _scopeDepth
                        };
                        _frameCount++;
                        fi = _frameCount - 1;
                        code = _frames[fi].Function.Chunk.Code;
                        constants = _frames[fi].Function.Chunk.Constants;
                        break;
                    }

                    case OpCode.TailCall:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var funcName = constants[nameIdx].TextValue;

                        var def = _script.DefinitionContext.GetScope().GetFunction(funcName, argCount);

                        // ── Fallback: check if the name is a variable holding a function value ──
                        CompiledFunction? tailLambda = null;
                        if (def == null)
                        {
                            var funcVal = _script.MemoryContext.GetScope().Get(funcName);
                            if (funcVal.IsNativeObject && funcVal.Ref is CompiledFunction cf)
                                tailLambda = cf;
                            else
                            {
                                RuntimeError(System.FormattableString.Invariant($"Function '{funcName}' with {argCount} args is not defined"));
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                        }

                        // Resolve the CompiledFunction to use (lambda or named)
                        var tailTarget = tailLambda ?? def!.Compiled;

                        if (tailTarget == null && def is NativeFunctionDefinition nativeTail)
                        {
                            // Can't tail-call into native — call normally and return the result
                            var args = CollectArgs(argCount);
                            WarValue nResult;
                            try { nResult = nativeTail.NativeBody(args); _script.ThrowPendingHostFault(); }
                            catch (System.Exception e)
                            {
                                RaiseNativeFailure("Native function", funcName, e);
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            // Behave as Return with this result
                            // Pop excess scopes
                            while (_scopeDepth > _frames[fi].SavedScopeDepth)
                            {
                                if (_script.AuditSink != null) AuditBlockEnd();
                                _script.MemoryContext.EndScope();
                                _scopeDepth--;
                            }
                            DoReturn(nResult, fi, ref code, ref constants, out fi);
                            if (_frameCount == 0) return;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else if (tailTarget != null && _frames[fi].Kind == FrameKind.Constructor)
                        {
                            // The compiler emits TailCall only inside functions;
                            // a constructor frame has no function scope to reuse.
                            RuntimeError("Tail call in a constructor");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        else if (tailTarget != null)
                        {
                            // ── True tail call: reuse the current frame ──

                            // The arguments are padded where they are, then
                            // moved down to the frame's base.
                            if (!StackFits(_sp - argCount, argCount, tailTarget))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }

                            // Pad missing arguments with null for default parameters.
                            var arity = tailTarget.Arity;
                            while (argCount < arity)
                            {
                                Push(WarValue.Null);
                                argCount++;
                            }

                            // Pop old function's memory scope (a method frame
                            // keeps its class context and gets a new local scope)
                            if (_frames[fi].Kind != FrameKind.Script)
                                _script.MemoryContext.EndScope();

                            // Push fresh scope for the new function
                            _script.MemoryContext.PushScope(
                                _script.MemoryContext.NewScope(_script.UserMemoryScope, MemoryScopeKind.FunctionLocal));

                            // Move arguments down to the frame's stack base
                            var stackBase = _frames[fi].StackBase;
                            var argBase = _sp - argCount;
                            for (int i = 0; i < argCount; i++)
                                _stack[stackBase + i] = _stack[argBase + i];
                            _sp = stackBase + argCount;

                            // Reuse the frame — no _frameCount change
                            _frames[fi].Function = tailTarget;
                            _frames[fi].IP = 0;
                            if (_frames[fi].Kind == FrameKind.Script)
                                _frames[fi].Kind = FrameKind.Function;
                            code = tailTarget.Chunk.Code;
                            constants = tailTarget.Chunk.Constants;
                        }
                        else
                        {
                            RuntimeError($"Function '{funcName}' is not compiled");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }

                    case OpCode.Return:
                    {
                        var result = Pop();

                        // A return leaves every ensure block of this frame it
                        // is inside: whatever they were going to re-raise or
                        // return is discarded (CRASH-7).
                        DropPendingOfFrame(fi);

                        // Check if there are any handlers on this frame with ensure blocks.
                        while (_handlerCount > 0 && _handlers[_handlerCount - 1].FrameIndex == fi)
                        {
                            var handler = _handlers[--_handlerCount];
                            while (_scopeDepth > handler.SavedScopeDepth)
                            {
                                if (_script.AuditSink != null) AuditBlockEnd();
                                _script.MemoryContext.EndScope();
                                _scopeDepth--;
                            }
                            if (handler.EnsureIP != handler.EndIP)
                            {
                                _sp = handler.SavedSP;
                                PushPending(true, result, fi, handler.EndIP);
                                _frames[fi].IP = handler.EnsureIP;
                                goto nextInstruction;
                            }
                        }

                        // Pop any remaining excess scopes (e.g. from rescue body PushScope
                        // where the handler was already consumed during exception handling)
                        while (_scopeDepth > _frames[fi].SavedScopeDepth)
                        {
                            if (_script.AuditSink != null) AuditBlockEnd();
                            _script.MemoryContext.EndScope();
                            _scopeDepth--;
                        }

                        DoReturn(result, fi, ref code, ref constants, out fi);
                        if (_frameCount == 0) return;
                        code = _frames[fi].Function.Chunk.Code;
                        constants = _frames[fi].Function.Chunk.Constants;
                        break;
                    nextInstruction:
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Method calls
                    // ══════════════════════════════════════════════

                    case OpCode.CallMethod:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var methodName = constants[nameIdx].TextValue;

                        // Stack layout: [..., instance, arg0, arg1, ...]
                        var instance = _stack[_sp - argCount - 1];
                        if (!instance.IsClass)
                        {
                            RuntimeError($"Cannot call method '{methodName}' on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        var classData = instance.ClassValue;
                        var classDef = FindClassWithMethod(classData.Definition, methodName, argCount);
                        if (classDef == null)
                        {
                            RuntimeError(System.FormattableString.Invariant($"Method '{methodName}' with {argCount} args not found"));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        var methodDef = classDef.GetDefinitionScope().GetFunction(methodName, argCount)!;
                        var methodClassData = classData.GetRelation(classDef.ClassDetails.Name) ?? classData;

                        if (methodDef.Compiled != null && !StackFits(_sp - argCount - 1, argCount, methodDef.Compiled))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        // Remove instance from under the args
                        var argBase = _sp - argCount - 1;
                        for (int i = 0; i < argCount; i++)
                            _stack[argBase + i] = _stack[argBase + i + 1];
                        _sp--;

                        if (methodDef is NativeFunctionDefinition nativeMethod)
                        {
                            var args = CollectArgs(argCount);
                            WarValue mResult;
                            try { mResult = nativeMethod.NativeBody(args); _script.ThrowPendingHostFault(); }
                            catch (System.Exception e)
                            {
                                RaiseNativeFailure("Native method", methodName, e);
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(mResult);
                        }
                        else
                        {
                            // Push class context
                            _script.DefinitionContext.PushScope(classDef.GetDefinitionScope());
                            _script.MemoryContext.PushScope(methodClassData.MemoryScope);
                            _script.ClassInstanceContext.PushValue(methodClassData);

                            if (methodDef.Compiled != null)
                            {
                                // Pad missing arguments with null for default parameters.
                                var arity = methodDef.Compiled.Arity;
                                while (argCount < arity)
                                {
                                    Push(WarValue.Null);
                                    argCount++;
                                }

                                // Push an inner scope for method-local variables
                                _script.MemoryContext.PushScope(_script.MemoryContext.NewScope(MemoryScopeKind.MethodLocal));

                                var newBase = _sp - argCount;
                                if (_frameCount >= FramesMax)
                                {
                                    _script.MemoryContext.EndScope(); // method-local
                                    _script.DefinitionContext.EndScope();
                                    _script.MemoryContext.EndScope(); // class memory
                                    _script.ClassInstanceContext.PopValue();
                                    RuntimeError("Stack overflow");
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                _frames[_frameCount] = new CallFrame
                                {
                                    Function = methodDef.Compiled,
                                    IP = 0,
                                    StackBase = newBase,
                                    Kind = FrameKind.Method,
                                    SavedScopeDepth = _scopeDepth
                                };
                                _frameCount++;
                                fi = _frameCount - 1;
                                code = _frames[fi].Function.Chunk.Code;
                                constants = _frames[fi].Function.Chunk.Constants;
                            }
                            else
                            {
                                // No bytecode — clean up context and report error
                                _script.DefinitionContext.EndScope();
                                _script.MemoryContext.EndScope();
                                _script.ClassInstanceContext.PopValue();
                                RuntimeError($"Method '{methodName}' is not compiled");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                        }
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Arrays
                    // ══════════════════════════════════════════════

                    case OpCode.NewArray:
                    {
                        var count = ReadU16(code, ref _frames[fi].IP);
                        if (!CheckArrayLength(count) || !TrackAlloc(EstimateArrayBytes(count)))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var list = new List<WarValue>(count);
                        var arrBase = _sp - count;
                        for (int i = 0; i < count; i++)
                            list.Add(_stack[arrBase + i]);
                        _sp = arrBase;
                        Push(WarValue.FromArray(list));
                        break;
                    }
                    case OpCode.IndexGet:
                    {
                        var index = Pop(); var target = Pop();
                        WarValue element;
                        if (target.IsArray && index.IsNumeric)
                            element = target.GetArrayElement(WarValue.ToInt(index.Numeric));
                        else if (target.IsText && index.IsNumeric)
                            element = target.GetTextChar(WarValue.ToInt(index.Numeric));
                        else
                            element = WarValue.Null;
                        if (element.IsNull && _script.AuditSink != null) AuditIndexRead(target, index, fi);
                        Push(element);
                        break;
                    }
                    case OpCode.IndexSet:
                    {
                        var value = Pop(); var index = Pop(); var target = Pop();
                        if (_script.AuditSink != null) AuditIndexWrite(target, index, textWriteBack: false, fi);
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement(WarValue.ToInt(index.Numeric), value);
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetLocal:
                    {
                        // Stack: [..., index, value]. Operand: local slot.
                        var slot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop(); var index = Pop();
                        var target = _stack[_frames[fi].StackBase + slot];
                        if (_script.AuditSink != null) AuditIndexWrite(target, index, textWriteBack: true, fi);
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement(WarValue.ToInt(index.Numeric), value);
                        else if (target.IsText && index.IsNumeric)
                        {
                            var replacement = Text(value);
                            if (!TextWriteFits(target, index, replacement))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var newText = target.SetTextChar(WarValue.ToInt(index.Numeric), replacement);
                            _stack[_frames[fi].StackBase + slot] = newText;
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetGlobal:
                    {
                        // Stack: [..., index, value]. Operand: name constant.
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
                        var value = Pop(); var index = Pop();
                        var target = _script.MemoryContext.GetScope().Get(name);
                        if (_script.AuditSink != null) AuditIndexWrite(target, index, textWriteBack: true, fi);
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement(WarValue.ToInt(index.Numeric), value);
                        else if (target.IsText && index.IsNumeric)
                        {
                            var replacement = Text(value);
                            if (!TextWriteFits(target, index, replacement))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var newText = target.SetTextChar(WarValue.ToInt(index.Numeric), replacement);
                            _script.MemoryContext.GetScope().Set(name, newText);
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetProp:
                    {
                        // Stack: [..., instance, index, value]. Operand: property name + cache slot.
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop(); var index = Pop(); var inst = Pop();
                        if (inst.IsClass)
                        {
                            var cd = inst.ClassValue;
                            var details = cd.Definition.ClassDetails;
                            ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                            WarValue propVal;
                            if (ReferenceEquals(cache.CachedType, details))
                            {
                                propVal = cd.GetPropertyByIndex(cache.CachedIndex);
                            }
                            else
                            {
                                var propName = constants[nameIdx].TextValue;
                                propVal = cd.GetProperty(propName);
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }

                            if (_script.AuditSink != null) AuditIndexWrite(propVal, index, textWriteBack: true, fi);
                            if (propVal.IsArray && index.IsNumeric)
                                propVal.SetArrayElement(WarValue.ToInt(index.Numeric), value);
                            else if (propVal.IsText && index.IsNumeric)
                            {
                                var replacement = Text(value);
                                if (!TextWriteFits(propVal, index, replacement))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                var newText = propVal.SetTextChar(WarValue.ToInt(index.Numeric), replacement);
                                if (ReferenceEquals(cache.CachedType, details))
                                    cd.SetPropertyByIndex(cache.CachedIndex, newText);
                                else
                                    cd.SetProperty(constants[nameIdx].TextValue, newText);
                            }
                        }
                        else if (_script.AuditSink != null)
                            Audit(AuditKind.IndexWriteInvalid, fi,
                                  Describe(inst) + " :: " + constants[nameIdx].TextValue + "{" + Describe(index) + "} = ...: not an instance");
                        Push(value);
                        break;
                    }
                    case OpCode.ArrayAppend:
                    {
                        var value = Pop(); var arr = Peek();
                        if (arr.IsArray)
                        {
                            if (!CheckArrayLength(arr.ArrayValue.Count + 1L) || !TrackAlloc(16)) // one WarValue slot
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            arr.ArrayAppend(value);
                        }
                        else if (_script.AuditSink != null)
                            Audit(AuditKind.AppendToNonArray, fi, Describe(arr) + " << " + Describe(value));
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Iteration helpers
                    // ══════════════════════════════════════════════

                    case OpCode.Len:
                    {
                        var target = Pop();
                        if (target.IsArray) Push(WarValue.FromNumeric(target.ArrayValue.Count));
                        else if (target.IsText) Push(WarValue.FromNumeric(target.TextValue.Length));
                        else if (target.IsClass)
                            Push(WarValue.FromNumeric(target.ClassValue.Definition.ClassDetails.Properties.Count));
                        else Push(WarValue.FromNumeric(0));
                        break;
                    }
                    case OpCode.IterPrepare:
                    {
                        var target = Pop();
                        if (target.IsArray)
                            Push(target);
                        else if (target.IsClass)
                        {
                            var cd = target.ClassValue;
                            var props = cd.Definition.ClassDetails.Properties;
                            if (!TrackAlloc(EstimateArrayBytes(props.Count)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var list = new List<WarValue>(props.Count);
                            for (int i = 0; i < props.Count; i++)
                                list.Add(cd.GetProperty(props[i]));
                            Push(WarValue.FromArray(list));
                        }
                        else
                        {
                            RuntimeError($"Unable to iterate '{target}'");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Classes
                    // ══════════════════════════════════════════════

                    case OpCode.NewInstance:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var className = constants[nameIdx].TextValue;
                        var result = InstantiateClass(className, argCount);
                        if (_script.ExceptionContext.IsRaised() || !EnterConstructors(result, ref fi, ref code, ref constants))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.NewNestedInstance:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var className = constants[nameIdx].TextValue;

                        // Stack: [..., parentInstance, arg0, arg1, ...]
                        var args = CollectArgs(argCount);
                        var parent = Pop();
                        if (!parent.IsClass)
                        {
                            RuntimeError("Cannot instantiate nested class on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        // Push parent's definition scope so the nested class (and
                        // its base classes) can be found while allocating.
                        WarValue result;
                        _script.DefinitionContext.PushScope(parent.ClassValue.Definition.GetDefinitionScope());
                        try
                        {
                            // Re-push args for InstantiateClass
                            foreach (var arg in args) Push(arg);
                            result = InstantiateClass(className, argCount);
                        }
                        finally
                        {
                            _script.DefinitionContext.EndScope();
                        }
                        // The constructor bodies look names up from their own
                        // class's definition scope (the top of DefinitionContext
                        // while they run), so they do not need the parent's.
                        if (_script.ExceptionContext.IsRaised() || !EnterConstructors(result, ref fi, ref code, ref constants))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.GetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        var inst = Pop();
                        if (inst.IsClass)
                        {
                            var cd = inst.ClassValue;
                            var details = cd.Definition.ClassDetails;
                            ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                            if (ReferenceEquals(cache.CachedType, details))
                            {
                                // Cache hit: O(1) array access
                                Push(cd.GetPropertyByIndex(cache.CachedIndex));
                            }
                            else
                            {
                                // Cache miss: dictionary lookup + update cache
                                var propName = constants[nameIdx].TextValue;
                                Push(cd.GetProperty(propName));
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }
                        }
                        else
                        {
                            var propName = constants[nameIdx].TextValue;
                            RuntimeError($"Cannot access property '{propName}' on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.SetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop(); var inst = Pop();
                        if (inst.IsClass)
                        {
                            var cd = inst.ClassValue;
                            var details = cd.Definition.ClassDetails;
                            ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                            if (ReferenceEquals(cache.CachedType, details))
                            {
                                cd.SetPropertyByIndex(cache.CachedIndex, value);
                            }
                            else
                            {
                                var propName = constants[nameIdx].TextValue;
                                if (_script.AuditSink != null) AuditPropertyWrite(cd, propName, fi);
                                cd.SetProperty(propName, value);
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }
                        }
                        else if (_script.AuditSink != null)
                            Audit(AuditKind.PropertyWriteOnNonInstance, fi,
                                  Describe(inst) + " :: " + constants[nameIdx].TextValue + " = " + Describe(value));
                        Push(value);
                        break;
                    }
                    case OpCode.This:
                        if (!_script.ClassInstanceContext.HasValue)
                        {
                            RuntimeError("'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(WarValue.FromClass(_script.ClassInstanceContext.GetValue()));
                        break;

                    // ── Superinstructions: This + GetProperty / SetProperty ──
                    case OpCode.ThisGetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        if (!_script.ClassInstanceContext.HasValue)
                        {
                            RuntimeError("'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var cd = _script.ClassInstanceContext.GetValue();
                        var details = cd.Definition.ClassDetails;
                        ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                        if (ReferenceEquals(cache.CachedType, details))
                        {
                            Push(cd.GetPropertyByIndex(cache.CachedIndex));
                        }
                        else
                        {
                            var propName = constants[nameIdx].TextValue;
                            Push(cd.GetProperty(propName));
                            if (details.PropertyIndex.TryGetValue(propName, out var idx))
                            {
                                cache.CachedType = details;
                                cache.CachedIndex = idx;
                            }
                        }
                        break;
                    }
                    case OpCode.ThisSetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop();
                        if (!_script.ClassInstanceContext.HasValue)
                        {
                            RuntimeError("'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var cd = _script.ClassInstanceContext.GetValue();
                        var details = cd.Definition.ClassDetails;
                        ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                        if (ReferenceEquals(cache.CachedType, details))
                        {
                            cd.SetPropertyByIndex(cache.CachedIndex, value);
                        }
                        else
                        {
                            var propName = constants[nameIdx].TextValue;
                            if (_script.AuditSink != null) AuditPropertyWrite(cd, propName, fi);
                            cd.SetProperty(propName, value);
                            if (details.PropertyIndex.TryGetValue(propName, out var idx))
                            {
                                cache.CachedType = details;
                                cache.CachedIndex = idx;
                            }
                        }
                        Push(value);
                        break;
                    }

                    case OpCode.CastAs:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var typeName = constants[nameIdx].TextValue;
                        var inst = Pop();
                        if (inst.IsClass)
                        {
                            var cd = inst.ClassValue;
                            if (cd.Definition.ClassDetails.Name == typeName) Push(inst);
                            else
                            {
                                var rel = cd.GetRelation(typeName);
                                Push(rel != null ? WarValue.FromClass(rel) : WarValue.Null);
                            }
                        }
                        else Push(WarValue.Null);
                        break;
                    }
                    case OpCode.InstanceOf:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var typeName = constants[nameIdx].TextValue;
                        var inst = Pop();
                        Push(WarValue.FromLogical(inst.IsClass && inst.ClassValue.ContainsRelation(typeName)));
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Builtins
                    // ══════════════════════════════════════════════

                    case OpCode.Print:
                    {
                        var printed = Pop();
                        if (_script.AuditSink != null && ContainsLogical(printed, 0))
                            Audit(AuditKind.LogicalFormatted, fi, "print " + Describe(printed));
                        _script.Print(Text(printed));
                        break;
                    }

                    case OpCode.Assert:
                    {
                        if (!IsTruthy(Pop()))
                        {
                            var line = _frames[fi].IP > 0 && _frames[fi].IP - 1 < code.Count
                                ? _frames[fi].Function.Chunk.Lines.GetLine(_frames[fi].IP - 1) : 0;
                            RuntimeError(System.FormattableString.Invariant($"Assertion error at line {line}"));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Exception handling
                    // ══════════════════════════════════════════════

                    case OpCode.PushHandler:
                    {
                        var rescueIP = ReadU16(code, ref _frames[fi].IP);
                        var ensureIP = ReadU16(code, ref _frames[fi].IP);
                        var endIP    = ReadU16(code, ref _frames[fi].IP);
                        var hasRescue = code[_frames[fi].IP++] != 0;
                        if (_handlerCount >= HandlersMax)
                        {
                            RuntimeError("Exception handler stack overflow (begin blocks nested too deeply)");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        _handlers[_handlerCount++] = new TryHandler
                        {
                            RescueIP = rescueIP,
                            EnsureIP = ensureIP,
                            EndIP = endIP,
                            FrameIndex = fi,
                            SavedSP = _sp,
                            HasRescue = hasRescue,
                            SavedScopeDepth = _scopeDepth
                        };
                        break;
                    }
                    case OpCode.PopHandler:
                        if (_handlerCount > 0) _handlerCount--;
                        break;
                    case OpCode.DropPending:
                    {
                        // 'break'/'next' leaves an ensure block: what it was
                        // going to re-raise or return is discarded (Q-12),
                        // as 'return' and 'raise' inside it already do.
                        var endIP = ReadU16(code, ref _frames[fi].IP);
                        if (_pendingCount > 0 && _pending[_pendingCount - 1].FrameIndex == fi
                            && _pending[_pendingCount - 1].EndIP == endIP)
                            ClearPending(_pendingCount - 1);
                        break;
                    }

                    case OpCode.Raise:
                    {
                        var val = Pop();
                        if (val.IsNull) val = WarValue.FromText("Empty exception");
                        // Preserve the original WarValue (could be a class instance)
                        _script.ExceptionContext.RaiseException(val);
                        if (DoHandleException(ref fi, ref code, ref constants)) break;
                        return;
                    }

                    // ══════════════════════════════════════════════
                    //  Coroutines
                    // ══════════════════════════════════════════════

                    case OpCode.Yield:
                        _yielded = true;
                        _yieldType = YieldType.NextTick;
                        _yieldWaitDuration = F64.Zero;
                        _script.SetYielded(YieldType.NextTick, F64.Zero);
                        return;
                    case OpCode.YieldWait:
                    {
                        var dur = Pop();
                        var d = dur.IsNumeric ? dur.Numeric : F64.Zero;
                        _yielded = true;
                        _yieldType = YieldType.Wait;
                        _yieldWaitDuration = d;
                        _script.SetYielded(YieldType.Wait, d);
                        return;
                    }

                    // ══════════════════════════════════════════════
                    //  Import
                    // ══════════════════════════════════════════════

                    case OpCode.Import:
                    {
                        var pathIdx = ReadU16(code, ref _frames[fi].IP);
                        ExecuteImport(constants[pathIdx].TextValue);
                        if (_script.ExceptionContext.IsRaised())
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }

                    // An opcode byte that is not an instruction (T1.7): a host
                    // fault. The loader's verifier is meant to make this
                    // unreachable; until it does, it stops misaligned execution.
                    default:
                        throw new System.InvalidOperationException(System.FormattableString.Invariant(
                            $"Invalid opcode {(byte)instruction} at offset {_frames[fi].IP - 1} in '{_frames[fi].Function.Name}'"));
                }

                // An ensure block entered with a re-raise or return to finish
                // has completed: finish it.
                if (_pendingCount > 0 && _pending[_pendingCount - 1].FrameIndex == fi
                    && _frames[fi].IP >= _pending[_pendingCount - 1].EndIP)
                {
                    var pending = _pending[--_pendingCount];
                    _pending[_pendingCount] = default;

                    if (!pending.IsReturn)
                    {
                        _script.ExceptionContext.RaiseException(pending.Value);
                        if (DoHandleException(ref fi, ref code, ref constants))
                            continue;
                        return;
                    }

                    var result = pending.Value;

                    // Check for more handlers on this frame
                    while (_handlerCount > 0 && _handlers[_handlerCount - 1].FrameIndex == fi)
                    {
                        var h = _handlers[--_handlerCount];
                        while (_scopeDepth > h.SavedScopeDepth)
                        {
                            if (_script.AuditSink != null) AuditBlockEnd();
                            _script.MemoryContext.EndScope();
                            _scopeDepth--;
                        }
                        if (h.EnsureIP != h.EndIP)
                        {
                            _sp = h.SavedSP;
                            PushPending(true, result, fi, h.EndIP);
                            _frames[fi].IP = h.EnsureIP;
                            goto continueLoop;
                        }
                    }

                    // Pop excess scopes before returning
                    while (_scopeDepth > _frames[fi].SavedScopeDepth)
                    {
                        if (_script.AuditSink != null) AuditBlockEnd();
                        _script.MemoryContext.EndScope();
                        _scopeDepth--;
                    }

                    DoReturn(result, fi, ref code, ref constants, out fi);
                    if (_frameCount == 0) return;
                    code = _frames[fi].Function.Chunk.Code;
                    constants = _frames[fi].Function.Chunk.Constants;
                    continueLoop:
                    continue;
                }

                // Check for exceptions raised by native calls
                if (_script.ExceptionContext.IsRaised())
                {
                    if (DoHandleException(ref fi, ref code, ref constants))
                        continue;
                    return;
                }
            }
        }

        private void DoReturn(in WarValue result, int fi,
            ref List<byte> code, ref List<WarValue> constants, out int newFi)
        {
            var kind = _frames[fi].Kind;

            _sp = _frames[fi].StackBase;
            _frameCount--;
            DropPendingOfFrame(fi);
            PopFrameContexts(fi, kind);

            // A constructor's value is discarded: the instance it initialized
            // is already on the stack, below the frame (T1.1).
            if (kind != FrameKind.Constructor)
            {
                if (_frameCount > 0)
                    Push(result);
                else
                    _topLevelResult = result;
            }

            newFi = _frameCount - 1;
        }

        // ────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────

        private DebugContext BuildDebugContext(int fi, int currentLine)
        {
            // Build call stack (outermost first)
            var callStack = new List<DebugContext.StackEntry>(_frameCount);
            for (int i = 0; i < _frameCount; i++)
            {
                var frame = _frames[i];
                var frameIP = frame.IP > 0 ? frame.IP - 1 : 0;
                var frameLine = frameIP < frame.Function.Chunk.Code.Count
                    ? frame.Function.Chunk.Lines.GetLine(frameIP) : 0;
                callStack.Add(new DebugContext.StackEntry(frame.Function.Name, frameLine));
            }
            // Update the current frame's line to the accurate one
            if (callStack.Count > 0)
                callStack[callStack.Count - 1] = new DebugContext.StackEntry(
                    _frames[fi].Function.Name, currentLine);

            // Build locals dictionary for the current frame
            var locals = new Dictionary<string, WarValue>();
            var func = _frames[fi].Function;
            var stackBase = _frames[fi].StackBase;
            var localCount = System.Math.Min(func.LocalNames.Length, _sp - stackBase);
            for (int i = 0; i < localCount; i++)
            {
                var name = i < func.LocalNames.Length ? func.LocalNames[i] : null;
                if (name == null || name.StartsWith("$", System.StringComparison.Ordinal)) continue;
                locals[name] = _stack[stackBase + i];
            }

            return new DebugContext(_script, _script.ScriptName, currentLine,
                _frames[fi].Function.Name, callStack, locals);
        }

        private void Push(in WarValue value) => _stack[_sp++] = value;
        private WarValue Pop() => _stack[--_sp];
        private WarValue Peek() => _stack[_sp - 1];

        private static int ReadU16(List<byte> code, ref int ip)
        {
            var hi = code[ip++];
            var lo = code[ip++];
            return (hi << 8) | lo;
        }

        private static bool IsTruthy(in WarValue val)
        {
            if (val.IsLogical) return val.LogicalValue;
            if (val.IsNull) return false;
            if (val.IsNumeric) return val.Numeric != 0;
            return true;
        }

        private List<WarValue> CollectArgs(int argCount)
        {
            var args = new List<WarValue>(argCount);
            var argBase = _sp - argCount;
            for (int i = 0; i < argCount; i++)
                args.Add(_stack[argBase + i]);
            _sp = argBase;
            return args;
        }

        /// <summary>
        /// Whether a frame of <paramref name="function"/> whose
        /// <paramref name="argCount"/> arguments start at
        /// <paramref name="stackBase"/> fits in the value stack (T1.7, SEC-2):
        /// the verifier bounds every height the function reaches by its
        /// <see cref="CompiledFunction.MaxStack"/>, counted from its arity
        /// (the null padding included). Arguments beyond the arity stay in the
        /// frame under everything the function pushes, so they are added. When
        /// it does not fit, raises "Value stack overflow" for the caller,
        /// before anything was pushed.
        /// </summary>
        private bool StackFits(int stackBase, int argCount, CompiledFunction function)
        {
            int surplus = argCount > function.Arity ? argCount - function.Arity : 0;
            if (stackBase + surplus + function.MaxStack <= StackMax) return true;
            RuntimeError("Value stack overflow");
            return false;
        }

        private void RuntimeError(string message)
        {
            _script.ExceptionContext.RaiseException(message);
        }

        /// <summary>
        /// Raises the script error for an exception thrown by a native body
        /// (plan 6.9, PROP-13): a <see cref="ScriptErrorException"/> keeps its
        /// own message; anything else is a NativeError naming the native. A
        /// host fault recorded by an entry nested inside the native is
        /// rethrown instead, as this VM's own host fault.
        /// </summary>
        private void RaiseNativeFailure(string what, string name, System.Exception e)
        {
            // A host fault in an entry the native made (plan 6.3, T2.4):
            // this VM stops too, whatever the native did with the result.
            _script.ThrowPendingHostFault();
            if (e is ScriptErrorException scriptError)
                _script.ExceptionContext.RaiseException(e.Message, scriptError.Kind);
            else
                _script.ExceptionContext.RaiseException($"{what} '{name}' failed: {e.Message}", ScriptErrorKind.NativeError);
        }

        /// <summary>
        /// Track a heap allocation. Returns true if within budget.
        /// If over budget, raises "Memory budget exceeded" and returns false.
        /// Call it before allocating; on false, allocate nothing and handle
        /// the exception (T1.4).
        /// </summary>
        private bool TrackAlloc(long bytes)
        {
            var budget = _budget;
            if (budget.MemoryLimit <= 0) return true;
            budget.MemoryUsed += bytes;
            if (budget.MemoryUsed <= budget.MemoryLimit) return true;
            RuntimeError("Memory budget exceeded");
            return false;
        }

        /// <summary>
        /// Allocation guard (T1.4): raises "Text too long" and returns false
        /// when a text of <paramref name="length"/> characters would exceed
        /// <see cref="WarScriptLanguage.MaxTextLength"/>. Call before allocating.
        /// </summary>
        private bool CheckTextLength(long length)
        {
            if (length <= _script.MaxTextLength) return true;
            RuntimeError("Text too long");
            return false;
        }

        /// <summary>
        /// Allocation guard (T1.4): raises "Array too long" and returns false
        /// when an array of <paramref name="length"/> elements would exceed
        /// <see cref="WarScriptLanguage.MaxArrayLength"/>. Call before allocating.
        /// </summary>
        private bool CheckArrayLength(long length)
        {
            if (length <= _script.MaxArrayLength) return true;
            RuntimeError("Array too long");
            return false;
        }

        /// <summary>
        /// Whether <c>text{index} = replacement</c> stays within the text
        /// limit. An index out of range changes nothing, so it always fits.
        /// </summary>
        private bool TextWriteFits(in WarValue text, in WarValue index, string replacement)
        {
            var i = WarValue.ToInt(index.Numeric);
            var length = text.TextValue.Length;
            if (i < 0 || i >= length) return true;
            return CheckTextLength(length - 1L + replacement.Length);
        }

        /// <summary>Estimate heap cost of a string (UTF-16 + object overhead).</summary>
        private static long EstimateTextBytes(long length) => length * 2L + 40;

        /// <summary>Estimate heap cost of a new array.</summary>
        private static long EstimateArrayBytes(long count) => count * 16L + 64;

        /// <summary>Estimate heap cost of a class instance.</summary>
        private static long EstimateClassBytes(int propertyCount) => propertyCount * 16L + 96;

        private bool DoHandleException(ref int fi, ref List<byte> code, ref List<WarValue> constants)
        {
            while (_handlerCount > 0)
            {
                var handler = _handlers[_handlerCount - 1];

                if (handler.FrameIndex != fi)
                {
                    // Unwind this frame
                    PopFrame(fi);
                    if (_frameCount == 0) return false;
                    fi = _frameCount - 1;
                    code = _frames[fi].Function.Chunk.Code;
                    constants = _frames[fi].Function.Chunk.Constants;
                    continue;
                }

                _handlerCount--;
                // The error leaves every ensure block entered inside this
                // handler's begin body: what they were going to re-raise or
                // return is superseded by this error (CRASH-7).
                DropPendingAbove(_handlerCount);
                var errorVal = _script.ExceptionContext.Exception?.Value ?? WarValue.Null;
                _sp = handler.SavedSP;

                // Pop any MemoryContext scopes pushed since the handler was installed
                while (_scopeDepth > handler.SavedScopeDepth)
                {
                    if (_script.AuditSink != null) AuditBlockEnd();
                    _script.MemoryContext.EndScope();
                    _scopeDepth--;
                }

                if (handler.HasRescue)
                {
                    // Has rescue: clear exception, push error, jump to rescue.
                    // With an ensure block, the rescue body runs under the
                    // same handler without its rescue, so leaving it by
                    // return, raise or an error still runs the ensure block
                    // (Q-14, SEM-13); the compiler's PopHandler at its end
                    // removes it. Without one it runs under no handler of
                    // the block (nothing to run, and recursion from it is
                    // not limited by HandlersMax). Either way the code falls
                    // through to the ensure block.
                    _script.ExceptionContext.RescueException();
                    if (handler.EnsureIP != handler.EndIP)
                    {
                        handler.HasRescue = false;
                        _handlers[_handlerCount++] = handler;
                    }
                    Push(errorVal);
                    _frames[fi].IP = handler.RescueIP;
                    return true;
                }
                else
                {
                    // No rescue, but ensure must run: clear the exception,
                    // keep its value, run ensure, re-raise when ensure ends.
                    _script.ExceptionContext.RescueException();
                    PushPending(false, errorVal, fi, handler.EndIP);
                    _frames[fi].IP = handler.EnsureIP;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pops frame <paramref name="fi"/> (the top one) during unwinding,
        /// with the block scopes, function scope and class context it pushed
        /// and any ensure records it owns. Its stack values are dropped.
        /// </summary>
        private void PopFrame(int fi)
        {
            while (_scopeDepth > _frames[fi].SavedScopeDepth)
            {
                if (_script.AuditSink != null) AuditBlockEnd();
                _script.MemoryContext.EndScope();
                _scopeDepth--;
            }
            var kind = _frames[fi].Kind;
            _sp = _frames[fi].StackBase;
            _frameCount--;
            DropPendingOfFrame(fi);
            PopFrameContexts(fi, kind);
        }

        /// <summary>
        /// Pops what a frame of <paramref name="kind"/> pushed on the context
        /// stacks when it was entered (see <see cref="FrameKind"/>).
        /// </summary>
        private void PopFrameContexts(int fi, FrameKind kind)
        {
            switch (kind)
            {
                case FrameKind.Function:
                    _script.MemoryContext.EndScope();
                    break;
                case FrameKind.Method:
                    _script.MemoryContext.EndScope(); // method-local
                    _script.DefinitionContext.EndScope();
                    _script.MemoryContext.EndScope(); // instance
                    _script.ClassInstanceContext.PopValue();
                    break;
                case FrameKind.Constructor:
                    _script.DefinitionContext.EndScope();
                    _script.MemoryContext.EndScope(); // instance
                    _script.ClassInstanceContext.PopValue();
                    _frames[fi].Instance = null;
                    break;
            }
        }

        // ── Pending ensure records ──

        /// <summary>Records an ensure block being entered with a return or re-raise to finish.</summary>
        private void PushPending(bool isReturn, in WarValue value, int fi, int endIP)
        {
            if (_pendingCount == _pending.Length)
                System.Array.Resize(ref _pending, _pending.Length * 2);
            _pending[_pendingCount++] = new PendingEnsure
            {
                IsReturn = isReturn,
                Value = value,
                FrameIndex = fi,
                EndIP = endIP,
                HandlerDepth = _handlerCount
            };
        }

        /// <summary>Discards the records of frame <paramref name="fi"/> and of any frame above it.</summary>
        private void DropPendingOfFrame(int fi)
        {
            var count = _pendingCount;
            while (count > 0 && _pending[count - 1].FrameIndex >= fi) count--;
            ClearPending(count);
        }

        /// <summary>Discards the records of ensure blocks entered while more than <paramref name="handlerCount"/> handlers were installed.</summary>
        private void DropPendingAbove(int handlerCount)
        {
            var count = _pendingCount;
            while (count > 0 && _pending[count - 1].HandlerDepth > handlerCount) count--;
            ClearPending(count);
        }

        private void ClearPending(int newCount)
        {
            for (int i = newCount; i < _pendingCount; i++)
                _pending[i] = default;
            _pendingCount = newCount;
        }

        // ── Array concatenation ──
        private static WarValue AddArrays(in WarValue a, in WarValue b)
        {
            if (a.IsArray && b.IsArray)
            {
                var lv = a.ArrayValue; var rv = b.ArrayValue;
                var list = new List<WarValue>(lv.Count + rv.Count);
                list.AddRange(lv); list.AddRange(rv);
                return WarValue.FromArray(list);
            }
            if (a.IsArray)
            {
                var lv = a.ArrayValue;
                var list = new List<WarValue>(lv.Count + 1);
                list.AddRange(lv); list.Add(b);
                return WarValue.FromArray(list);
            }
            var rv2 = b.ArrayValue;
            var list2 = new List<WarValue>(rv2.Count + 1);
            list2.Add(a); list2.AddRange(rv2);
            return WarValue.FromArray(list2);
        }

        // ── Class instantiation (T1.1) ──
        //
        // 'new' happens in two steps. The allocation step builds the whole
        // instance graph (the derived ClassData, one ClassData per base class
        // in Relations, the ValueReferences shared with base classes, the
        // property values from the arguments) without running any code, and
        // lists the constructor bodies to run in execution order: base
        // classes depth-first in BaseTypes order, then the derived class.
        // EnterConstructors then pushes the instance as the expression's value
        // and one Constructor frame per body on this VM, so constructors are
        // ordinary frames: they count against FramesMax and the budgets, and
        // their errors unwind to the code that executed 'new'.

        // Constructors to run for the instance being allocated, in execution
        // order. Filled by the allocation step, emptied by EnterConstructors.
        private readonly List<ClassData> _constructors = new List<ClassData>();

        // Classes on the current base-class path of the allocation step, to
        // detect a class that inherits from itself (CRASH-5).
        private readonly List<ClassDefinition> _allocationPath = new List<ClassDefinition>();

        /// <summary>
        /// Allocation step for <c>new className [args]</c>. Returns the
        /// instance, or default with an error raised (and nothing queued).
        /// </summary>
        private WarValue InstantiateClass(string className, int argCount)
        {
            var args = CollectArgs(argCount);
            var definition = _script.DefinitionContext.GetScope().GetClass(className);
            if (definition == null)
            {
                RuntimeError($"Class '{className}' is not defined");
                return default;
            }
            _constructors.Clear();
            _allocationPath.Clear();
            var classData = AllocateInstance(definition, args, new Dictionary<string, ClassData>(), null);
            _allocationPath.Clear();
            if (classData == null)
            {
                _constructors.Clear();
                return default;
            }
            return WarValue.FromClass(classData);
        }

        /// <summary>
        /// Allocates one class part of an instance and, recursively, its base
        /// classes, queuing their constructor bodies in execution order. When
        /// sharedRefs is non-null, property slots map to the derived class's
        /// refs. Returns null with an error raised on failure.
        /// </summary>
        private ClassData? AllocateInstance(ClassDefinition definition, List<WarValue> args,
            Dictionary<string, ClassData> relations, ValueReference[]? sharedRefs)
        {
            var propCount = definition.ClassDetails.Properties.Count;
            if (!TrackAlloc(EstimateClassBytes(propCount))) return null;

            var valueRefs = new ValueReference[propCount];
            for (int i = 0; i < propCount; i++)
            {
                if (sharedRefs != null && i < sharedRefs.Length && sharedRefs[i] != null)
                    valueRefs[i] = sharedRefs[i]; // share the same reference
                else
                    valueRefs[i] = ValueReference.InstanceOf(i < args.Count ? args[i] : WarValue.Null);
            }

            var classScope = new MemoryScope(_script, null, false, MemoryScopeKind.ClassInstance);
            var classData = new ClassData(definition, classScope, relations);
            classData.PropertyValues = valueRefs;
            relations[definition.ClassDetails.Name] = classData;
            for (int i = 0; i < propCount; i++)
                classScope.SetLocal(definition.ClassDetails.Properties[i], valueRefs[i]);

            // Base classes: pass shared ValueReferences
            _allocationPath.Add(definition);
            foreach (var baseType in definition.BaseTypes)
            {
                var baseDef = _script.DefinitionContext.GetScope().GetClass(baseType.Name);
                if (baseDef == null) continue;
                if (_allocationPath.Contains(baseDef))
                {
                    // Q-11: a rescuable error at 'new', not a compile error.
                    RuntimeError($"Class '{baseDef.ClassDetails.Name}' inherits from itself");
                    return null;
                }

                // baseType.Properties lists derived-class property names passed to the
                // base constructor. The mapping is positional:
                //   baseType.Properties[i] → baseDef.ClassDetails.Properties[i]
                var basePropCount = baseDef.ClassDetails.Properties.Count;
                var baseShared = new ValueReference[basePropCount];
                for (int i = 0; i < baseType.Properties.Count && i < basePropCount; i++)
                {
                    var derivedPropName = baseType.Properties[i];
                    var derivedIdx = definition.ClassDetails.Properties.IndexOf(derivedPropName);
                    if (derivedIdx >= 0 && derivedIdx < valueRefs.Length)
                        baseShared[i] = valueRefs[derivedIdx];
                }

                var baseArgs = new List<WarValue>();
                for (int i = 0; i < basePropCount; i++)
                    baseArgs.Add(baseShared[i] != null ? baseShared[i].Value : WarValue.Null);

                if (AllocateInstance(baseDef, baseArgs, relations, baseShared) == null) return null;
            }
            _allocationPath.RemoveAt(_allocationPath.Count - 1);

            // No tree-walk fallback: the AST is discarded after compilation.
            // Classes loaded via LoadBytecode have CompiledConstructor or nothing.
            if (definition.CompiledConstructor != null)
                _constructors.Add(classData);
            return classData;
        }

        /// <summary>
        /// Second step of <c>new</c>: pushes <paramref name="instance"/> as
        /// the expression's value, then one Constructor frame per queued
        /// constructor body, in reverse execution order so that the first to
        /// run is on top. Each frame pushes its class context now; frames and
        /// contexts are both last-in first-out, so each body runs with its own
        /// context on top. Returns false, with "Stack overflow" raised and
        /// the stack unchanged, when the frames do not all fit.
        /// </summary>
        private bool EnterConstructors(in WarValue instance,
            ref int fi, ref List<byte> code, ref List<WarValue> constants)
        {
            var count = _constructors.Count;
            if (_frameCount + count > FramesMax)
            {
                _constructors.Clear();
                RuntimeError("Stack overflow");
                return false;
            }
            // Every constructor frame's base is just above the instance.
            for (int i = 0; i < count; i++)
            {
                if (!StackFits(_sp + 1, 0, _constructors[i].Definition.CompiledConstructor))
                {
                    _constructors.Clear();
                    return false;
                }
            }

            Push(instance);
            if (count == 0) return true;

            for (int i = count - 1; i >= 0; i--)
            {
                var part = _constructors[i];
                var definition = part.Definition;
                _script.DefinitionContext.PushScope(definition.GetDefinitionScope());
                _script.MemoryContext.PushScope(part.MemoryScope);
                _script.ClassInstanceContext.PushValue(part);
                _frames[_frameCount++] = new CallFrame
                {
                    Function = definition.CompiledConstructor,
                    IP = 0,
                    StackBase = _sp,
                    Kind = FrameKind.Constructor,
                    Instance = part,
                    SavedScopeDepth = _scopeDepth
                };
            }
            _constructors.Clear();

            fi = _frameCount - 1;
            code = _frames[fi].Function.Chunk.Code;
            constants = _frames[fi].Function.Chunk.Constants;
            return true;
        }

        private static ClassDefinition? FindClassWithMethod(ClassDefinition classDef, string name, int arity)
        {
            var scope = classDef.GetDefinitionScope();
            if (scope.ContainsFunction(name, arity)) return classDef;
            foreach (var baseType in classDef.BaseTypes)
            {
                var baseDef = scope.GetClass(baseType.Name);
                if (baseDef == null) continue;
                var result = FindClassWithMethod(baseDef, name, arity);
                if (result != null) return result;
            }
            return null;
        }

        // ── Ordering ──

        /// <summary>
        /// <see cref="WarValue.CompareTo"/>, with the number case first and
        /// the audit events for the cases v2 rejects (plan 8.1).
        /// </summary>
        private int Compare(in WarValue a, in WarValue b, int fi)
        {
            if (a.IsNumeric && b.IsNumeric) return a.Numeric.CompareTo(b.Numeric);
            if (_script.AuditSink != null && !(a.IsText && b.IsText))
            {
                if (a.IsLogical && b.IsLogical)
                    Audit(AuditKind.LogicalOrdering, fi, Describe(a) + " <=> " + Describe(b));
                else
                    Audit(AuditKind.MixedTypeOrdering, fi, Describe(a) + " <=> " + Describe(b) + " (compared as text)");
            }
            return a.CompareTo(b);
        }

        // ── Audit mode (plan 8.1, T0.8; temporary) ──
        // Every caller checks _script.AuditSink != null first, so with no
        // sink attached the cost is that one check.

        private void Audit(AuditKind kind, int fi, string detail)
        {
            var sink = _script.AuditSink;
            if (sink == null) return;
            var function = _frames[fi].Function;
            var line = AuditLine(function.Chunk, _frames[fi].IP - 1);
            sink(new AuditEvent(kind, function.Chunk.Source ?? _script.ScriptName, line, function.Name, detail));
        }

        /// <summary>
        /// The line of the instruction at <paramref name="ip"/>. v1 records
        /// line 0 for many expression instructions (T1.10), so fall back to
        /// the next instruction that has a line, normally the statement the
        /// expression belongs to (its opcode comes after its operands), and
        /// then to the previous one.
        /// </summary>
        private static int AuditLine(Chunk chunk, int ip)
        {
            if (ip < 0 || ip >= chunk.Code.Count) return 0;
            var lines = chunk.Lines;
            int at = lines.EntryIndex(ip);
            if (at < 0) return 0;
            for (var i = at; i < lines.EntryCount; i++)
                if (lines.LineAt(i) > 0) return lines.LineAt(i);
            for (var i = at - 1; i >= 0; i--)
                if (lines.LineAt(i) > 0) return lines.LineAt(i);
            return 0;
        }

        /// <summary>
        /// A value as text for <c>print</c> and the text operators: the
        /// canonical format (T1.6), with the instance's native formatters.
        /// </summary>
        private string Text(in WarValue v) => v.Format(_script.NativeFormatters);

        /// <summary>
        /// A value for an audit detail: its type and a short form. Never calls
        /// ToString on an array, instance or native object (a cyclic array
        /// would recurse, CRASH-1; host objects are not formatted, R1).
        /// </summary>
        private static string Describe(in WarValue v)
        {
            switch (v.Tag)
            {
                case ValueTag.Null: return "null";
                case ValueTag.Numeric:
                    return "number " + WarValue.FormatNumber(v.Numeric); // culture-invariant (T1.6)
                case ValueTag.Logical: return "logical " + (v.LogicalValue ? "true" : "false");
                case ValueTag.Text:
                    var t = v.TextValue;
                    return "text \"" + (t.Length <= 40 ? t : t.Substring(0, 37) + "...") + "\"";
                case ValueTag.Array: return "array of " + v.ArrayValue.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ValueTag.Class: return v.ClassValue.Definition.ClassDetails.Name + " instance";
                case ValueTag.NativeObject: return "native " + (v.Ref?.GetType().Name ?? "null");
                default: return v.Tag.ToString();
            }
        }

        /// <summary>True if formatting <paramref name="v"/> writes a logical value (inside arrays too, to a fixed depth).</summary>
        private static bool ContainsLogical(in WarValue v, int depth)
        {
            if (v.IsLogical) return true;
            if (!v.IsArray || depth >= 8) return false;
            foreach (var element in v.ArrayValue)
                if (ContainsLogical(element, depth + 1)) return true;
            return false;
        }

        private void AuditMixedEquality(in WarValue a, in WarValue b, string op, bool result, int fi) =>
            Audit(AuditKind.MixedTypeEquality, fi,
                  Describe(a) + " " + op + " " + Describe(b) + " (v1: " + (result ? "true" : "false") + ")");

        private void AuditClassEquality(in WarValue a, in WarValue b, string op, int fi)
        {
            if (ReferenceEquals(a.Ref, b.Ref)) return;
            Audit(AuditKind.ClassStructuralEquality, fi,
                  Describe(a) + " " + op + " " + Describe(b) + ": two instances, structurally equal");
        }

        private void AuditConcatenation(in WarValue a, in WarValue b, int fi)
        {
            if (!a.IsText && !b.IsText)
                Audit(AuditKind.NonTextConcatenation, fi, Describe(a) + " + " + Describe(b));
            if (a.IsLogical || b.IsLogical)
                Audit(AuditKind.LogicalFormatted, fi, Describe(a) + " + " + Describe(b));
        }

        private void AuditIndexRead(in WarValue target, in WarValue index, int fi)
        {
            string why;
            if (!target.IsArray && !target.IsText) why = "not indexable";
            else if (!index.IsNumeric) why = "index is not a number";
            else
            {
                var i = WarValue.ToInt(index.Numeric);
                var count = target.IsArray ? target.ArrayValue.Count : target.TextValue.Length;
                if (i >= 0 && i < count) return; // an element that is null
                why = "out of bounds";
            }
            Audit(AuditKind.IndexReadInvalid, fi, Describe(target) + "{" + Describe(index) + "}: " + why);
        }

        /// <param name="textWriteBack">The opcode writes a changed text back to its variable or property.</param>
        private void AuditIndexWrite(in WarValue target, in WarValue index, bool textWriteBack, int fi)
        {
            var site = Describe(target) + "{" + Describe(index) + "} = ...";
            if (target.IsArray && index.IsNumeric)
            {
                var i = WarValue.ToInt(index.Numeric);
                if (i < 0 || i >= target.ArrayValue.Count)
                    Audit(AuditKind.IndexWriteInvalid, fi, site + ": out of bounds, ignored");
            }
            else if (textWriteBack && target.IsText && index.IsNumeric)
            {
                var i = WarValue.ToInt(index.Numeric);
                var ignored = i < 0 || i >= target.TextValue.Length;
                Audit(AuditKind.TextIndexAssign, fi, site + (ignored ? ": out of bounds, ignored" : ": text rewritten"));
            }
            else
            {
                var why = !index.IsNumeric && (target.IsArray || target.IsText) ? "index is not a number" : "not indexable";
                Audit(AuditKind.IndexWriteInvalid, fi, site + ": " + why + ", ignored");
            }
        }

        /// <summary>A property write on an instance that misses the inline cache: does it create an undeclared field?</summary>
        private void AuditPropertyWrite(ClassData cd, string name, int fi)
        {
            if (cd.Definition.ClassDetails.PropertyIndex.ContainsKey(name) || cd.MemoryScope.HasLocal(name)) return;
            // Assignments in the class body (the constructor) declare fields.
            if (_frames[fi].Function.Name.EndsWith("#ctor", StringComparison.Ordinal)) return;
            Audit(AuditKind.DynamicPropertyCreated, fi,
                  cd.Definition.ClassDetails.Name + " :: " + name + ": not a field of the class");
        }

        /// <summary>
        /// A global read: a method reading a field by its bare name, or a null
        /// that is undefined, a global hidden from a class, or a variable of
        /// an ended block.
        /// </summary>
        private void AuditGlobalRead(string name, bool readNull, int fi)
        {
            var start = _script.MemoryContext.GetScope();
            if (!readNull && start.Kind != MemoryScopeKind.MethodLocal) return;
            var holder = start.Resolve(name);
            if (holder != null)
            {
                if (start.Kind == MemoryScopeKind.MethodLocal && holder.Kind == MemoryScopeKind.ClassInstance)
                    Audit(AuditKind.FieldAccessViaBareName, fi, "read " + name);
                return; // a variable that holds null
            }

            if ((start.Kind == MemoryScopeKind.MethodLocal || start.Kind == MemoryScopeKind.ClassInstance)
                && _script.UserMemoryScope.Resolve(name) != null)
                Audit(AuditKind.GlobalShadowedInClass, fi,
                      name + ": null here, global value " + Describe(_script.UserMemoryScope.Get(name)));
            else if ((start.Kind == MemoryScopeKind.User || start.Kind == MemoryScopeKind.Block)
                     && _script.AuditBlockNames != null && _script.AuditBlockNames.Contains(name))
                Audit(AuditKind.BlockVariableEscapes, fi, name + ": assigned in an ended top-level block, read as null");
            else
                Audit(AuditKind.UndefinedRead, fi, name);
        }

        /// <summary>A global write, before it happens: from a function into a global, or from a method into a field?</summary>
        private void AuditGlobalWrite(string name, int fi)
        {
            var start = _script.MemoryContext.GetScope();
            if (start.HasLocal(name)) return;
            var holder = start.Resolve(name);
            if (holder == null) return; // creates a local of the current scope
            if (start.Kind == MemoryScopeKind.FunctionLocal
                && (holder.Kind == MemoryScopeKind.User || holder.Kind == MemoryScopeKind.Native))
                Audit(AuditKind.GlobalWriteFromFunction, fi, name);
            else if (start.Kind == MemoryScopeKind.MethodLocal && holder.Kind == MemoryScopeKind.ClassInstance)
                Audit(AuditKind.FieldAccessViaBareName, fi, "write " + name);
        }

        /// <summary>A top-level block ends: remember the names it created, for BlockVariableEscapes.</summary>
        private void AuditBlockEnd()
        {
            var scope = _script.MemoryContext.GetScope();
            if (scope.Kind != MemoryScopeKind.Block || scope.LocalNames.Count == 0) return;
            // Only blocks of the script body: a class body's blocks sit on the instance's scope.
            var outer = scope.Parent;
            while (outer != null && outer.Kind == MemoryScopeKind.Block) outer = outer.Parent;
            if (outer == null || outer.Kind != MemoryScopeKind.User) return;
            var names = _script.AuditBlockNames ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in scope.LocalNames) names.Add(name);
        }

        // ── Import ──
        private void ExecuteImport(string path)
        {
            if (_script.ImportStack.Contains(path)) { RuntimeError($"Circular import: '{path}'"); return; }
            if (_script.ImportCache.TryGetValue(path, out var cached))
            {
                cached.CopyLocalDefinitionsTo(_script.DefinitionContext.GetScope());
                return;
            }

            var callerScope = _script.DefinitionContext.GetScope();

            // Precompiled first: a host that ships bytecode for this path has
            // already done the lex/parse/compile work this would otherwise
            // repeat at every load. Null means there is none to use.
            var precompiled = _script.LoadImportedBytecode(path, callerScope, out var importDefScope);

            string? source = null;
            if (precompiled == null)
            {
                if (_script.FileResolver == null) { RuntimeError($"Cannot import '{path}': no file resolver"); return; }
                try { source = _script.FileResolver(path); }
                catch (System.Exception e) { RuntimeError($"Failed to read import '{path}': {e.Message}"); return; }
                if (source == null) { RuntimeError($"Import '{path}' not found"); return; }
            }

            _script.ImportStack.Add(path);
            importDefScope ??= _script.DefinitionContext.NewScope();
            _script.DefinitionContext.PushScope(importDefScope);
            try
            {
                var compiled = precompiled;
                if (compiled == null)
                {
                    var tokens = LexicalParser.Parse(source!);
                    var importStmt = new CompositeStatement(_script, null, path);
                    StatementParser.Parse(_script, tokens, importStmt);
                    compiled = Compiler.CompileScript(_script, importStmt, importDefScope);
                }
                new WarVM(_script).Run(compiled);
                if (!_script.ExceptionContext.IsRaised())
                    _script.ImportCache[path] = importDefScope;
            }
            finally
            {
                _script.DefinitionContext.EndScope();
                _script.ImportStack.Remove(path);
            }
            if (!_script.ExceptionContext.IsRaised())
                importDefScope.CopyLocalDefinitionsTo(callerScope);
        }
    }
}
