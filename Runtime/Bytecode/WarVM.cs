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
        }

        private struct PendingEnsure
        {
            /// <summary>True: return <see cref="Value"/>; false: re-raise it.</summary>
            public bool IsReturn;
            public WarValue Value;
            /// <summary>The error to re-raise (kind and call stack kept, T2.5); null for a return.</summary>
            public Exception.Exception? Error;
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
        private int _entryDefinitionDepth;

        /// <summary>
        /// An import whose top-level code runs in a <see cref="FrameKind.Import"/>
        /// frame of this VM (T3.7): what finishing it (the frame returns) or
        /// abandoning it (the frame is unwound, or the VM reset) must do.
        /// Import frames nest, so the innermost one owns the last record.
        /// </summary>
        private struct RunningImport
        {
            public string Path;
            /// <summary>The import's definitions (cached once it has run).</summary>
            public DefinitionScope Scope;
            /// <summary>The definitions of the frame that executed the import.</summary>
            public DefinitionScope CallerScope;
            /// <summary>Resolved by <c>Compile</c>: its definitions are already the script's.</summary>
            public bool InNamespace;
        }

        /// <summary>The imports running in this VM, outermost first; null until the first.</summary>
        private List<RunningImport>? _runningImports;

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
            System.Array.Clear(_frames, 0, _frameCount); // drop function and instance references
            _frameCount = 0;
            AbandonRunningImports();
            _handlerCount = 0;
            _topLevelResult = default;
            ClearPending(0);
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
            if (!StackFits(0, main)) return;
            EnterArguments(0, main);

            _frames[0] = new CallFrame
            {
                Function = main,
                IP = 0,
                StackBase = 0,
                Definitions = _script.DefinitionContext.GetScope()
            };
            _frameCount = 1;

            Execute();
        }

        /// <summary>
        /// Run a compiled function with pre-supplied arguments.
        /// Used by the Call() API for host→script invocations (tick loops, events).
        /// The arguments are copied onto the VM stack; the span is not kept.
        /// </summary>
        public WarValue RunFunction(CompiledFunction func, ReadOnlySpan<WarValue> arguments)
        {
            Reset();
            RecordEntryDepths();
            if (!StackFits(0, func)) return _topLevelResult;

            // Extra arguments are not copied; EnterArguments pads the
            // missing ones and reserves the body's locals.
            int given = System.Math.Min(arguments.Length, func.Arity);
            for (int i = 0; i < given; i++)
                _stack[_sp++] = arguments[i];
            EnterArguments(given, func);

            _frames[0] = new CallFrame
            {
                Function = func,
                IP = 0,
                StackBase = 0,
                Definitions = _script.DefinitionContext.GetScope()
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
            if (!StackFits(0, func)) return;

            int given = System.Math.Min(arguments.Length, func.Arity);
            for (int i = 0; i < given; i++)
                _stack[_sp++] = arguments[i];
            EnterArguments(given, func);

            _frames[0] = new CallFrame
            {
                Function = func,
                IP = 0,
                StackBase = 0,
                Definitions = _script.DefinitionContext.GetScope()
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
            _entryDefinitionDepth = _script.DefinitionContext.Depth;
        }

        /// <summary>
        /// Runs the dispatch loop: the VM's exception boundary (plan T1.3).
        /// <list type="bullet">
        /// <item>A <see cref="ScriptErrorException"/> (thrown by runtime code
        /// on the VM's behalf) becomes a script error that <c>begin/rescue</c>
        /// can catch. The push helper itself is branch-free (hot path): every
        /// frame push checks the callee's verified <c>MaxStack</c> against
        /// the room left (T1.7), so no push runs past the value stack.</item>
        /// <item>An error no handler catches unwinds every frame and stays
        /// raised for the
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
        /// Pops every frame, after an error that no handler caught. The error itself
        /// stays raised.
        /// </summary>
        private void UnwindAll()
        {
            while (_frameCount > 0)
                PopFrame(_frameCount - 1);
            _sp = 0;
            _handlerCount = 0;
            ClearPending(0);
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
            while (_script.DefinitionContext.Depth > _entryDefinitionDepth)
                _script.DefinitionContext.EndScope();
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
                    // The same error each time, so it keeps where the budget
                    // ran out (T2.5).
                    if (budget.Exhausted != null)
                        _script.ExceptionContext.Reraise(budget.Exhausted);
                    else
                    {
                        RuntimeError(ScriptErrorKind.InstructionBudgetExceeded, "Instruction budget exceeded");
                        budget.Exhausted = _script.ExceptionContext.Exception;
                    }
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
                        // DEC-5, 5.6: reading a name that is neither a local
                        // nor defined at runtime (a field for a constructor
                        // frame, a global, a host global) raises NameError.
                        if (!NameScope(fi).TryGet(name, out var global))
                        {
                            if (_script.AuditSink != null) Audit(AuditKind.UndefinedRead, fi, name);
                            RuntimeError(ScriptErrorKind.NameError, UndefinedVariableMessage(name));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(global);
                        break;
                    }
                    case OpCode.SetGlobal:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
                        // In a function, method or lambda every local is a
                        // stack slot, and a class body writes its fields with
                        // ThisSetProperty (T3.4): SetGlobal there is a write
                        // to a name declared `global`. Everywhere it writes
                        // the globals' scope chain (globals, then host
                        // globals), creating the name in the globals.
                        _script.UserMemoryScope.Set(name, Peek());
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Arithmetic
                    // ══════════════════════════════════════════════

                    // Operand types follow the strict table of plan 5.3
                    // (T3.6, DEC-8, PROP-6, PROP-17): anything outside it
                    // raises TypeError "Cannot apply '<op>' to <type> and
                    // <type>" (OperandError); there is no coercion through
                    // text except for '+' with a text operand.

                    case OpCode.Add:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsText || b.IsText)
                        {
                            var left = Text(a); var right = Text(b);
                            long length = (long)left.Length + right.Length;
                            if (!CheckTextLength(length) || !TrackAlloc(EstimateTextBytes(length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromText(_interner.Intern(left + right)));
                        }
                        else if (a.IsNumeric && b.IsNumeric)
                            Push(WarValue.FromNumeric(a.Numeric + b.Numeric));
                        else if (a.IsArray && b.IsArray)
                        {
                            long length = (long)a.ArrayValue.Count + b.ArrayValue.Count;
                            if (!CheckArrayLength(length) || !TrackAlloc(EstimateArrayBytes(length)))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(AddArrays(a, b));
                        }
                        else
                        {
                            OperandError("+", a, b);
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
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
                            // No text subtraction in v2: Text_remove (PROP-7).
                            OperandError("-", a, b);
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
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
                            OperandError("*", a, b);
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
                                RuntimeError(ScriptErrorKind.DivisionByZero, "Division by zero");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromNumeric(a.Numeric / b.Numeric));
                        }
                        else
                        {
                            OperandError("/", a, b);
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
                                RuntimeError(ScriptErrorKind.DivisionByZero, "Modulo by zero");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(WarValue.FromNumeric(a.Numeric % b.Numeric));
                        }
                        else
                        {
                            OperandError("%", a, b);
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        break;
                    }
                    case OpCode.Negate:
                    {
                        var v = Pop();
                        if (!v.IsNumeric)
                        {
                            RuntimeError(ScriptErrorKind.TypeError, "Cannot apply '-' to " + TypeName(v));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(WarValue.FromNumeric(-v.Numeric));
                        break;
                    }

                    // ══════════════════════════════════════════════
                    //  Comparison
                    // ══════════════════════════════════════════════

                    // Equality (plan 5.4): values of different types are
                    // not equal and raise nothing; null equals only null;
                    // instances are equal when they are the same instance
                    // (ClassData.Root, PROP-8).
                    case OpCode.Equal:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(a.Equals(b)));
                        break;
                    }
                    case OpCode.NotEqual:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(!a.Equals(b)));
                        break;
                    }
                    case OpCode.Less:
                    case OpCode.LessEqual:
                    case OpCode.Greater:
                    case OpCode.GreaterEqual:
                    {
                        var b = Pop(); var a = Pop();
                        if (!Compare(a, b, instruction, out var order))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(WarValue.FromLogical(instruction == OpCode.Less ? order < 0
                            : instruction == OpCode.LessEqual ? order <= 0
                            : instruction == OpCode.Greater ? order > 0
                            : order >= 0));
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
                        if (!Compare(a, b, OpCode.Less, out var order))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        if (!(order < 0))
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

                        var def = _frames[fi].Definitions.GetFunction(funcName, argCount);

                        // ── Fallback: check if the name is a variable holding a function value ──
                        CompiledFunction? lambdaFunc = null;
                        if (def == null)
                        {
                            var fv = AsFunctionValue(NameScope(fi).Get(funcName));
                            if (fv != null)
                            {
                                if (!OwnedHere(fv.Owner, "'" + funcName + "'"))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                lambdaFunc = fv.Function;
                            }
                            else
                            {
                                RuntimeError(ScriptErrorKind.NameError, System.FormattableString.Invariant($"Function '{funcName}' with {argCount} args is not defined"));
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                        }

                        if (lambdaFunc != null)
                        {
                            // Call a function value (lambda or function reference)
                            if (!StackFits(_sp - argCount, lambdaFunc))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            if (_frameCount >= FramesMax)
                            {
                                RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var newBase = EnterArguments(argCount, lambdaFunc);
                            _frames[_frameCount] = new CallFrame
                            {
                                Function = lambdaFunc,
                                IP = 0,
                                StackBase = newBase,
                                Kind = FrameKind.Function,
                                Instance = _frames[fi].Instance,
                                Definitions = _frames[fi].Definitions
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
                            // Arguments are already on the stack at sp-argCount .. sp-1.
                            // Missing ones are padded with null for default
                            // parameters (the body's desugared null-checks
                            // assign the defaults).
                            if (!StackFits(_sp - argCount, def.Compiled))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            if (_frameCount >= FramesMax)
                            {
                                RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            var newBase = EnterArguments(argCount, def.Compiled);
                            _frames[_frameCount] = new CallFrame
                            {
                                Function = def.Compiled,
                                IP = 0,
                                StackBase = newBase,
                                Kind = FrameKind.Function,
                                Instance = _frames[fi].Instance,
                                Definitions = _frames[fi].Definitions
                            };
                            _frameCount++;
                            fi = _frameCount - 1;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else
                        {
                            RuntimeError(ScriptErrorKind.TypeError, $"Function '{funcName}' is not compiled");
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
                        var calledValue = AsFunctionValue(_stack[_sp - argCount - 1]);

                        if (calledValue == null)
                        {
                            RuntimeError(ScriptErrorKind.TypeError, "Value is not callable");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        if (!OwnedHere(calledValue.Owner, "<fun " + (calledValue.Name == "<lambda>" ? "lambda" : calledValue.Name) + ">"))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var lambdaCF = calledValue.Function;

                        if (!StackFits(_sp - argCount - 1, lambdaCF))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        if (_frameCount >= FramesMax)
                        {
                            RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        // Remove the function value from under the args
                        var cvArgBase = _sp - argCount - 1;
                        for (int i = 0; i < argCount; i++)
                            _stack[cvArgBase + i] = _stack[cvArgBase + i + 1];
                        _sp--;

                        var cvNewBase = EnterArguments(argCount, lambdaCF);
                        _frames[_frameCount] = new CallFrame
                        {
                            Function = lambdaCF,
                            IP = 0,
                            StackBase = cvNewBase,
                            Kind = FrameKind.Function,
                            Instance = _frames[fi].Instance,
                            Definitions = _frames[fi].Definitions
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

                        var def = _frames[fi].Definitions.GetFunction(funcName, argCount);

                        // ── Fallback: check if the name is a variable holding a function value ──
                        CompiledFunction? tailLambda = null;
                        if (def == null)
                        {
                            var fv = AsFunctionValue(NameScope(fi).Get(funcName));
                            if (fv != null)
                            {
                                if (!OwnedHere(fv.Owner, "'" + funcName + "'"))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                tailLambda = fv.Function;
                            }
                            else
                            {
                                RuntimeError(ScriptErrorKind.NameError, System.FormattableString.Invariant($"Function '{funcName}' with {argCount} args is not defined"));
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
                            DoReturn(nResult, fi, ref code, ref constants, out fi);
                            if (_frameCount == 0) return;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else if (tailTarget != null && _frames[fi].Kind == FrameKind.Constructor)
                        {
                            // The compiler emits TailCall only inside functions;
                            // a constructor frame has no function scope to reuse.
                            RuntimeError(ScriptErrorKind.TypeError, "Tail call in a constructor");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        else if (tailTarget != null)
                        {
                            // ── True tail call: reuse the current frame ──

                            // The arguments are moved down to the frame's
                            // base, then padded (or cut) to the arity and
                            // followed by the reserved locals. The frame's
                            // base is at or below where they are now.
                            var stackBase = _frames[fi].StackBase;
                            if (!StackFits(stackBase, tailTarget))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }

                            // A method frame keeps its class definitions;
                            // no frame has a memory scope of its own (T3.5).
                            var argBase = _sp - argCount;
                            for (int i = 0; i < argCount; i++)
                                _stack[stackBase + i] = _stack[argBase + i];
                            _sp = stackBase + argCount;
                            EnterArguments(argCount, tailTarget);

                            // Reuse the frame — no _frameCount change
                            _frames[fi].Function = tailTarget;
                            _frames[fi].IP = 0;
                            code = tailTarget.Chunk.Code;
                            constants = tailTarget.Chunk.Constants;
                        }
                        else
                        {
                            RuntimeError(ScriptErrorKind.TypeError, $"Function '{funcName}' is not compiled");
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
                            if (handler.EnsureIP != handler.EndIP)
                            {
                                _sp = handler.SavedSP;
                                PushPending(true, result, fi, handler.EndIP);
                                _frames[fi].IP = handler.EnsureIP;
                                goto nextInstruction;
                            }
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
                            RuntimeError(ScriptErrorKind.TypeError, $"Cannot call method '{methodName}' on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        var classData = instance.ClassValue;
                        // Fields of a foreign instance are plain data; its
                        // methods are not (6.7, Q-1).
                        if (!OwnedHere(classData.Owner, "method '" + methodName + "'"))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var classDef = FindClassWithMethod(classData.Definition, methodName, argCount);
                        if (classDef == null)
                        {
                            RuntimeError(ScriptErrorKind.NameError, System.FormattableString.Invariant($"Method '{methodName}' with {argCount} args not found"));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        var methodDef = classDef.GetDefinitionScope().GetFunction(methodName, argCount)!;
                        var methodClassData = classData.GetRelation(classDef.ClassDetails.Name) ?? classData;

                        if (methodDef.Compiled != null && !StackFits(_sp - argCount - 1, methodDef.Compiled))
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
                            if (methodDef.Compiled != null)
                            {
                                // A bare name in a method is a local or a
                                // global (T3.4, DEC-6, PROP-20; NameScope);
                                // fields are `@x` / `this :: x`.
                                if (_frameCount >= FramesMax)
                                {
                                    RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                var newBase = EnterArguments(argCount, methodDef.Compiled);
                                _frames[_frameCount] = new CallFrame
                                {
                                    Function = methodDef.Compiled,
                                    IP = 0,
                                    StackBase = newBase,
                                    Kind = FrameKind.Method,
                                    Instance = methodClassData,
                                    Definitions = classDef.GetDefinitionScope()
                                };
                                _frameCount++;
                                fi = _frameCount - 1;
                                code = _frames[fi].Function.Chunk.Code;
                                constants = _frames[fi].Function.Chunk.Constants;
                            }
                            else
                            {
                                // No bytecode — report error
                                RuntimeError(ScriptErrorKind.TypeError, $"Method '{methodName}' is not compiled");
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
                        if (!CheckIndex(target, index, write: false, out var i))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(target.IsArray
                            ? target.ArrayValue[i]
                            : WarValue.FromText(target.TextValue.Substring(i, 1)));
                        break;
                    }
                    case OpCode.IndexSet:
                    {
                        var value = Pop(); var index = Pop(); var target = Pop();
                        if (!CheckIndex(target, index, write: true, out var i))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        target.ArrayValue[i] = value;
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetLocal:
                    {
                        // Stack: [..., index, value]. Operand: local slot.
                        var slot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop(); var index = Pop();
                        var target = _stack[_frames[fi].StackBase + slot];
                        if (!CheckIndex(target, index, write: true, out var i))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        target.ArrayValue[i] = value;
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetGlobal:
                    {
                        // Stack: [..., index, value]. Operand: name constant.
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
                        var value = Pop(); var index = Pop();
                        var nameScope = NameScope(fi);
                        if (!nameScope.TryGet(name, out var target))
                        {
                            if (_script.AuditSink != null) Audit(AuditKind.UndefinedRead, fi, name);
                            RuntimeError(ScriptErrorKind.NameError, UndefinedVariableMessage(name));
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        if (!CheckIndex(target, index, write: true, out var i))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        target.ArrayValue[i] = value;
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
                                if (!CheckDeclared(cd, propName))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                propVal = cd.GetProperty(propName);
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }

                            if (!CheckIndex(propVal, index, write: true, out var i))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            propVal.ArrayValue[i] = value;
                        }
                        else
                        {
                            RuntimeError(ScriptErrorKind.TypeError, "Cannot access property '" + constants[nameIdx].TextValue + "' on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
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
                        else
                        {
                            OperandError("<<", arr, value);
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
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
                            RuntimeError(ScriptErrorKind.TypeError, $"Unable to iterate '{target}'");
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
                        var result = InstantiateClass(className, argCount, _frames[fi].Definitions);
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
                            RuntimeError(ScriptErrorKind.TypeError, "Cannot instantiate nested class on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        // Its constructor is the other instance's code (6.7, Q-1).
                        if (!OwnedHere(parent.ClassValue.Owner, "the constructor of nested class '" + className + "'"))
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        // The nested class (and its base classes) are
                        // looked up from the parent's definition scope.
                        // Re-push args for InstantiateClass
                        foreach (var arg in args) Push(arg);
                        var result = InstantiateClass(className, argCount,
                            parent.ClassValue.Definition.GetDefinitionScope());
                        // The constructor bodies look names up from their own
                        // class's definition scope (their frames' Definitions),
                        // so they do not need the parent's.
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
                                if (!CheckDeclared(cd, propName))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
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
                            RuntimeError(ScriptErrorKind.TypeError, $"Cannot access property '{propName}' on non-class value");
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
                                if (!CheckDeclared(cd, propName))
                                {
                                    if (DoHandleException(ref fi, ref code, ref constants)) break;
                                    return;
                                }
                                cd.SetProperty(propName, value);
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }
                        }
                        else
                        {
                            // A property write on a non-instance (DEC-10, 5.5).
                            RuntimeError(ScriptErrorKind.TypeError, "Cannot set property '" + constants[nameIdx].TextValue + "' on non-class value");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.This:
                    {
                        var thisData = _frames[fi].Instance;
                        if (thisData == null)
                        {
                            RuntimeError(ScriptErrorKind.NameError, "'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(WarValue.FromClass(thisData));
                        break;
                    }

                    // ── Superinstructions: This + GetProperty / SetProperty ──
                    case OpCode.ThisGetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
                        var cd = _frames[fi].Instance;
                        if (cd == null)
                        {
                            RuntimeError(ScriptErrorKind.NameError, "'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var details = cd.Definition.ClassDetails;
                        ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                        if (ReferenceEquals(cache.CachedType, details))
                        {
                            Push(cd.GetPropertyByIndex(cache.CachedIndex));
                        }
                        else
                        {
                            var propName = constants[nameIdx].TextValue;
                            if (!CheckDeclared(cd, propName))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
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
                        var cd = _frames[fi].Instance;
                        if (cd == null)
                        {
                            RuntimeError(ScriptErrorKind.NameError, "'this' can only be used inside a class");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        var details = cd.Definition.ClassDetails;
                        ref var cache = ref _frames[fi].Function.Chunk.PropertyCaches[cacheSlot];
                        if (ReferenceEquals(cache.CachedType, details))
                        {
                            cd.SetPropertyByIndex(cache.CachedIndex, value);
                        }
                        else
                        {
                            var propName = constants[nameIdx].TextValue;
                            if (!CheckDeclared(cd, propName))
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
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
                        _script.Print(Text(Pop()));
                        break;
                    }

                    case OpCode.Assert:
                    {
                        if (!IsTruthy(Pop()))
                        {
                            var line = _frames[fi].IP > 0 && _frames[fi].IP - 1 < code.Count
                                ? _frames[fi].Function.Chunk.Lines.GetLine(_frames[fi].IP - 1) : 0;
                            RuntimeError(ScriptErrorKind.AssertionFailed, System.FormattableString.Invariant($"Assertion error at line {line}"));
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
                            RuntimeError(ScriptErrorKind.LimitExceeded, "Exception handler stack overflow (begin blocks nested too deeply)");
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
                            HasRescue = hasRescue
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
                        if (ExecuteImport(constants[pathIdx].TextValue, fi))
                        {
                            fi = _frameCount - 1;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else if (_script.ExceptionContext.IsRaised())
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
                        if (pending.Error != null) _script.ExceptionContext.Reraise(pending.Error);
                        else _script.ExceptionContext.RaiseException(pending.Value);
                        if (DoHandleException(ref fi, ref code, ref constants))
                            continue;
                        return;
                    }

                    var result = pending.Value;

                    // Check for more handlers on this frame
                    while (_handlerCount > 0 && _handlers[_handlerCount - 1].FrameIndex == fi)
                    {
                        var h = _handlers[--_handlerCount];
                        if (h.EnsureIP != h.EndIP)
                        {
                            _sp = h.SavedSP;
                            PushPending(true, result, fi, h.EndIP);
                            _frames[fi].IP = h.EnsureIP;
                            goto continueLoop;
                        }
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
            ReleaseFrame(fi);
            if (kind == FrameKind.Import) FinishImport();

            // A constructor's value is discarded: the instance it initialized
            // is already on the stack, below the frame (T1.1); so is an
            // import's (T3.7).
            if (kind != FrameKind.Constructor && kind != FrameKind.Import)
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
        /// Whether a frame of <paramref name="function"/> based at (or above)
        /// <paramref name="stackBase"/> fits in the value stack (T1.7, SEC-2):
        /// the verifier bounds every height the function reaches by its
        /// <see cref="CompiledFunction.MaxStack"/>, counted from its arity
        /// plus its reserved locals (the null padding included). Arguments
        /// beyond the arity are dropped (<see cref="EnterArguments"/>), so they
        /// take no room. When it does not fit, raises "Value stack overflow"
        /// for the caller, before anything was pushed.
        /// </summary>
        private bool StackFits(int stackBase, CompiledFunction function)
        {
            if (stackBase + function.MaxStack <= StackMax) return true;
            RuntimeError(ScriptErrorKind.LimitExceeded, "Value stack overflow");
            return false;
        }

        /// <summary>
        /// Lays out a new frame of <paramref name="function"/> whose
        /// <paramref name="argCount"/> arguments are on top of the stack and
        /// returns its stack base (T3.5): arguments beyond the arity are
        /// dropped (a function value called with more arguments than it has
        /// parameters, as the host <c>Call</c> ignores extra ones), missing
        /// ones are null (so default parameters apply), and then the body's
        /// <see cref="CompiledFunction.ReservedLocals"/> slots are pushed as
        /// null. So the parameters and body locals are exactly the slots the
        /// compiler gave them. The caller has checked <see cref="StackFits"/>.
        /// </summary>
        private int EnterArguments(int argCount, CompiledFunction function)
        {
            int stackBase = _sp - argCount;
            int arity = function.Arity;
            if (argCount > arity)
                _sp = stackBase + arity;
            else
                for (; argCount < arity; argCount++)
                    _stack[_sp++] = WarValue.Null;
            for (int i = function.ReservedLocals; i > 0; i--)
                _stack[_sp++] = WarValue.Null;
            return stackBase;
        }

        private void RuntimeError(ScriptErrorKind kind, string message)
        {
            _script.ExceptionContext.RaiseException(message, kind);
        }

        /// <summary>
        /// Raises the script error for an exception thrown by a native body
        /// (plan 6.9, PROP-13): a <see cref="ScriptErrorException"/> keeps its
        /// own message (a <see cref="NativeArgumentException"/> from
        /// <see cref="NativeHelper"/> keeps v1's, naming the native, with its
        /// own kind); anything else is a NativeError naming the native. A
        /// host fault recorded by an entry nested inside the native is
        /// rethrown instead, as this VM's own host fault.
        /// </summary>
        private void RaiseNativeFailure(string what, string name, System.Exception e)
        {
            // A host fault in an entry the native made (plan 6.3, T2.4):
            // this VM stops too, whatever the native did with the result.
            _script.ThrowPendingHostFault();
            if (e is NativeArgumentException argumentError)
                _script.ExceptionContext.RaiseException($"{what} '{name}' failed: {e.Message}", argumentError.Kind);
            else if (e is ScriptErrorException scriptError)
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
            RuntimeError(ScriptErrorKind.MemoryBudgetExceeded, "Memory budget exceeded");
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
            RuntimeError(ScriptErrorKind.LimitExceeded, "Text too long");
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
            RuntimeError(ScriptErrorKind.LimitExceeded, "Array too long");
            return false;
        }

        /// <summary>
        /// Checks <c>target{index}</c> (plan 5.5, DEC-10, T3.6): the target
        /// must be an array, or text when reading (text is immutable, so a
        /// write raises TypeError); the index must be a number, truncated
        /// toward zero (D1), and inside the bounds, else IndexError. On
        /// success <paramref name="i"/> is the element's position; otherwise
        /// the error is raised and the caller handles it.
        /// </summary>
        private bool CheckIndex(in WarValue target, in WarValue index, bool write, out int i)
        {
            i = 0;
            int length;
            if (target.IsArray)
                length = target.ArrayValue.Count;
            else if (target.IsText && !write)
                length = target.TextValue.Length;
            else
            {
                RuntimeError(ScriptErrorKind.TypeError, target.IsText
                    ? "Cannot assign to an index of text: text is immutable"
                    : "Cannot index " + TypeName(target));
                return false;
            }
            if (!index.IsNumeric)
            {
                RuntimeError(ScriptErrorKind.TypeError, "Index must be a number, not " + TypeName(index));
                return false;
            }
            i = WarValue.ToInt(index.Numeric);
            if (i < 0 || i >= length)
            {
                RuntimeError(ScriptErrorKind.IndexError,
                    System.FormattableString.Invariant($"Index {i} is out of bounds (length {length})"));
                return false;
            }
            return true;
        }

        /// <summary>
        /// A value's type as error messages name it (plan 5.3): <c>null</c>,
        /// <c>number</c>, <c>logical</c>, <c>text</c>, <c>array</c>,
        /// <c>instance of C</c>, <c>native T</c>.
        /// </summary>
        internal static string TypeName(in WarValue v)
        {
            switch (v.Tag)
            {
                case ValueTag.Null: return "null";
                case ValueTag.Numeric: return "number";
                case ValueTag.Logical: return "logical";
                case ValueTag.Text: return "text";
                case ValueTag.Array: return "array";
                case ValueTag.Class: return "instance of " + v.ClassValue.Definition.ClassDetails.Name;
                default: return "native " + (v.Ref?.GetType().Name ?? "null");
            }
        }

        /// <summary>Estimate heap cost of a string (UTF-16 + object overhead).</summary>
        private static long EstimateTextBytes(long length) => length * 2L + 40;

        /// <summary>Estimate heap cost of a new array.</summary>
        private static long EstimateArrayBytes(long count) => count * 16L + 64;

        /// <summary>Estimate heap cost of a class instance.</summary>
        private static long EstimateClassBytes(int propertyCount) => propertyCount * 16L + 96;

        private bool DoHandleException(ref int fi, ref List<byte> code, ref List<WarValue> constants)
        {
            CaptureCallStack();
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
                    // keep it, run ensure, re-raise it when ensure ends.
                    var raised = _script.ExceptionContext.Exception;
                    _script.ExceptionContext.RescueException();
                    PushPending(false, errorVal, fi, handler.EndIP, raised);
                    _frames[fi].IP = handler.EnsureIP;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Records where the raised error is, before any frame is unwound
        /// (plan T2.5): this VM's frames, innermost first, with the line of
        /// the instruction each one is executing. An error that a nested
        /// entry left raised already has the frames of the VM it was raised
        /// in, and gets this VM's added below them as it continues from the
        /// native call site; one this VM recorded already (re-raised by an
        /// ensure block) keeps its stack.
        /// </summary>
        private void CaptureCallStack()
        {
            var raised = _script.ExceptionContext.Exception;
            if (raised == null || ReferenceEquals(raised.StackOwner, this)) return;
            var stack = raised.BeginFrames(this);
            for (int i = _frameCount - 1; i >= 0; i--)
            {
                var function = _frames[i].Function;
                var chunk = function.Chunk;
                var ip = _frames[i].IP > 0 ? _frames[i].IP - 1 : 0;
                var line = ip < chunk.Code.Count ? chunk.Lines.GetLine(ip) : 0;
                var column = ip < chunk.Code.Count ? chunk.Lines.GetColumn(ip) : 0;
                stack.Add(new ScriptFrame(function.Name, chunk.Source ?? _script.ScriptName, line, column));
            }
        }

        /// <summary>
        /// Pops frame <paramref name="fi"/> (the top one) during unwinding,
        /// with any ensure records it owns. Its stack values are dropped.
        /// </summary>
        private void PopFrame(int fi)
        {
            var kind = _frames[fi].Kind;
            _sp = _frames[fi].StackBase;
            _frameCount--;
            DropPendingOfFrame(fi);
            ReleaseFrame(fi);
            if (kind == FrameKind.Import) AbandonImport();
        }

        /// <summary>
        /// Where a bare name that is not a local resolves in frame
        /// <paramref name="fi"/> (T3.5): a constructor frame's instance fields
        /// (whose parent is the globals, so a field is found first, then a
        /// global; 5.7), every other frame's the globals (then host globals).
        /// There is no scope stack (T3.5 removed <c>MemoryContext</c>).
        /// </summary>
        private MemoryScope NameScope(int fi)
        {
            return _frames[fi].Kind == FrameKind.Constructor
                ? _frames[fi].Instance.MemoryScope
                : _script.UserMemoryScope;
        }

        /// <summary>
        /// Drops the references of popped frame <paramref name="fi"/>, so it
        /// keeps neither its instance nor its definitions alive. No frame
        /// pushes anything on the context stacks (T3.5).
        /// </summary>
        private void ReleaseFrame(int fi)
        {
            _frames[fi].Instance = null;
            _frames[fi].Definitions = null;
        }

        // ── Pending ensure records ──

        /// <summary>Records an ensure block being entered with a return or re-raise to finish.</summary>
        private void PushPending(bool isReturn, in WarValue value, int fi, int endIP,
            Exception.Exception? error = null)
        {
            if (_pendingCount == _pending.Length)
                System.Array.Resize(ref _pending, _pending.Length * 2);
            _pending[_pendingCount++] = new PendingEnsure
            {
                IsReturn = isReturn,
                Value = value,
                Error = error,
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
            var lv = a.ArrayValue; var rv = b.ArrayValue;
            var list = new List<WarValue>(lv.Count + rv.Count);
            list.AddRange(lv); list.AddRange(rv);
            return WarValue.FromArray(list);
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

        // Fields of the instance being allocated that a base-class parameter
        // created without binding them to an argument (Q-16: two such
        // parameters of one name are one field, not a conflict).
        private readonly HashSet<ValueReference> _unboundRefs = new HashSet<ValueReference>();

        /// <summary>
        /// The Q-16 error: base class <paramref name="baseClass"/> binds its
        /// parameter <paramref name="name"/> to an argument other than the one
        /// the field of that name of a <paramref name="className"/> instance
        /// is already bound to. Shared by the compiler and the VM.
        /// </summary>
        internal static string BoundTwiceMessage(string name, string className, string baseClass) =>
            $"'{name}' is bound twice in class '{className}': base class '{baseClass}' binds its parameter '{name}' to a different argument";

        /// <summary>The NameError message for reading a name that is not defined (5.6).</summary>
        internal static string UndefinedVariableMessage(string name) => "undefined variable '" + name + "'";

        /// <summary>
        /// The TypeError message for calling code that another instance owns
        /// (plan 6.7, Q-1): <paramref name="what"/> names the call, e.g.
        /// <c>method 'speak'</c>.
        /// </summary>
        internal static string ForeignOwnerMessage(string what) =>
            "Cannot call " + what + ": it belongs to another script instance";

        /// <summary>
        /// Whether code owned by <paramref name="owner"/> may run in this VM's
        /// instance (plan 6.7, Q-1): it must be the same instance, or have no
        /// owner (built by the host). Otherwise raises TypeError
        /// (<see cref="ForeignOwnerMessage"/>).
        /// </summary>
        private bool OwnedHere(WarScriptLanguage? owner, string what)
        {
            if (owner == null || ReferenceEquals(owner, _script)) return true;
            RuntimeError(ScriptErrorKind.TypeError, ForeignOwnerMessage(what));
            return false;
        }

        /// <summary>
        /// The function value <paramref name="value"/> holds, or null: a
        /// <see cref="FunctionValue"/> in a <c>NativeObject</c>.
        /// </summary>
        private static FunctionValue? AsFunctionValue(in WarValue value) =>
            value.IsNativeObject ? value.Ref as FunctionValue : null;

        /// <summary>
        /// Allocation step for <c>new className [args]</c>: the class and its
        /// base classes are looked up from <paramref name="scope"/> (the
        /// definitions of the frame executing <c>new</c>, or the parent
        /// instance's class for a nested class). Returns the instance, or
        /// default with an error raised (and nothing queued).
        /// </summary>
        private WarValue InstantiateClass(string className, int argCount, DefinitionScope scope)
        {
            var args = CollectArgs(argCount);
            var definition = scope.GetClass(className);
            if (definition == null)
            {
                RuntimeError(ScriptErrorKind.NameError, $"Class '{className}' is not defined");
                return default;
            }
            _constructors.Clear();
            _allocationPath.Clear();
            var classData = AllocateInstance(definition, scope, args, new Dictionary<string, ClassData>(), null, null, null);
            _allocationPath.Clear();
            _unboundRefs.Clear();
            if (classData == null)
            {
                _constructors.Clear();
                return default;
            }
            return WarValue.FromClass(classData);
        }

        /// <summary>
        /// Allocates one class part of an instance and, recursively, its base
        /// classes, queuing their constructor bodies in execution order.
        /// Every part shares the instance's one field scope
        /// (<paramref name="fields"/>, created for the derived class: T3.4,
        /// 5.7), so a field one class body assigns is a field of the whole
        /// instance. When sharedRefs is non-null (a base class), parameter i
        /// is bound to sharedRefs[i], the ValueReference of the derived-class
        /// parameter passed in that position, or to nothing (null) when that
        /// argument is not a parameter of the derived class. A base parameter
        /// named like a field that is already bound to something else raises
        /// the rescuable TypeError of Q-16 (the compiler rejects it where it
        /// knows the base classes). Every part gets <paramref name="root"/>
        /// (null for the derived class, which is its own root) as its
        /// <see cref="ClassData.Root"/>, including a diamond base allocated
        /// twice, whose first part the relation map no longer holds (5.4,
        /// PROP-8). Returns null with an error raised on failure.
        /// </summary>
        private ClassData? AllocateInstance(ClassDefinition definition, DefinitionScope scope, List<WarValue> args,
            Dictionary<string, ClassData> relations, ValueReference?[]? sharedRefs, MemoryScope? fields, ClassData? root)
        {
            var propCount = definition.ClassDetails.Properties.Count;
            if (!TrackAlloc(EstimateClassBytes(propCount))) return null;

            // The instance's fields. Their parent is the instance globals, so a
            // bare read in a class body finds a field first, then a global
            // (T3.4, 5.7, PROP-20).
            fields ??= new MemoryScope(_script, _script.UserMemoryScope, MemoryScopeKind.ClassInstance);
            var valueRefs = new ValueReference[propCount];
            for (int i = 0; i < propCount; i++)
            {
                var name = definition.ClassDetails.Properties[i];
                if (sharedRefs == null)
                {
                    // The class being instantiated: its parameters take the arguments.
                    valueRefs[i] = ValueReference.InstanceOf(i < args.Count ? args[i] : WarValue.Null);
                    fields.SetLocal(name, valueRefs[i]);
                    continue;
                }
                // A derived-class parameter that is itself bound to nothing
                // passes nothing on.
                var bound = i < sharedRefs.Length ? sharedRefs[i] : null;
                var unbound = bound == null || _unboundRefs.Contains(bound);
                var existing = fields.GetLocalReference(name);
                if (existing == null)
                {
                    if (bound == null)
                    {
                        bound = ValueReference.InstanceOf(WarValue.Null);
                        _unboundRefs.Add(bound);
                    }
                    fields.SetLocal(name, bound);
                    valueRefs[i] = bound;
                }
                else if (unbound ? _unboundRefs.Contains(existing) : ReferenceEquals(existing, bound))
                    valueRefs[i] = existing; // the same binding: one field
                else
                {
                    RuntimeError(ScriptErrorKind.TypeError, BoundTwiceMessage(name, _allocationPath[0].ClassDetails.Name,
                        definition.ClassDetails.Name));
                    return null;
                }
            }

            var classData = new ClassData(definition, fields, relations, _script);
            classData.PropertyValues = valueRefs;
            // Every part is the same instance for == (5.4, PROP-8).
            root ??= classData;
            classData.Root = root;
            relations[definition.ClassDetails.Name] = classData;

            // Base classes: pass shared ValueReferences
            _allocationPath.Add(definition);
            foreach (var baseType in definition.BaseTypes)
            {
                var baseDef = scope.GetClass(baseType.Name);
                if (baseDef == null) continue;
                if (_allocationPath.Contains(baseDef))
                {
                    // Q-11: a rescuable error at 'new', not a compile error.
                    RuntimeError(ScriptErrorKind.TypeError, $"Class '{baseDef.ClassDetails.Name}' inherits from itself");
                    return null;
                }

                // baseType.Properties lists derived-class property names passed to the
                // base constructor. The mapping is positional:
                //   baseType.Properties[i] → baseDef.ClassDetails.Properties[i]
                var basePropCount = baseDef.ClassDetails.Properties.Count;
                var baseShared = new ValueReference?[basePropCount];
                for (int i = 0; i < baseType.Properties.Count && i < basePropCount; i++)
                {
                    var derivedPropName = baseType.Properties[i];
                    var derivedIdx = definition.ClassDetails.Properties.IndexOf(derivedPropName);
                    if (derivedIdx >= 0 && derivedIdx < valueRefs.Length)
                        baseShared[i] = valueRefs[derivedIdx];
                }

                if (AllocateInstance(baseDef, scope, args, relations, baseShared, fields, root) == null) return null;
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
        /// run is on top. Each frame carries its class's definition scope and
        /// its instance part. Returns false, with "Stack overflow" raised and
        /// the stack unchanged, when the frames do not all fit.
        /// </summary>
        private bool EnterConstructors(in WarValue instance,
            ref int fi, ref List<byte> code, ref List<WarValue> constants)
        {
            var count = _constructors.Count;
            if (_frameCount + count > FramesMax)
            {
                _constructors.Clear();
                RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                return false;
            }
            // Every constructor frame's base is just above the instance.
            for (int i = 0; i < count; i++)
            {
                if (!StackFits(_sp + 1, _constructors[i].Definition.CompiledConstructor))
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
                _frames[_frameCount++] = new CallFrame
                {
                    Function = definition.CompiledConstructor,
                    IP = 0,
                    StackBase = _sp,
                    Kind = FrameKind.Constructor,
                    Instance = part,
                    Definitions = definition.GetDefinitionScope()
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
        /// Orders two values for <paramref name="op"/> (plan 5.3, PROP-17):
        /// numbers with numbers, text with text (ordinal). Any other pair,
        /// logical values included, raises TypeError and returns false.
        /// </summary>
        private bool Compare(in WarValue a, in WarValue b, OpCode op, out int order)
        {
            if (a.IsNumeric && b.IsNumeric)
            {
                order = a.Numeric.CompareTo(b.Numeric);
                return true;
            }
            if (a.IsText && b.IsText)
            {
                order = string.Compare(a.TextValue, b.TextValue, StringComparison.Ordinal);
                return true;
            }
            order = 0;
            OperandError(op == OpCode.LessEqual ? "<=" : op == OpCode.Greater ? ">"
                : op == OpCode.GreaterEqual ? ">=" : "<", a, b);
            return false;
        }

        /// <summary>
        /// Raises the TypeError of a binary operator whose operands the
        /// table of plan 5.3 does not accept (T3.6, DEC-8).
        /// </summary>
        private void OperandError(string op, in WarValue a, in WarValue b) =>
            RuntimeError(ScriptErrorKind.TypeError,
                "Cannot apply '" + op + "' to " + TypeName(a) + " and " + TypeName(b));

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
        /// A formatter is host code and may call back into the instance: a
        /// host fault in that nested entry stops this VM too (plan 6.3),
        /// whether the formatter returned or threw.
        /// </summary>
        private string Text(in WarValue v)
        {
            try { return v.Format(_script.NativeFormatters); }
            finally { _script.ThrowPendingHostFault(); }
        }

        /// <summary>
        /// PROP-15 (5.5, T3.6): <paramref name="name"/> must be a field of the
        /// instance <paramref name="cd"/> is a part of, that is, one that
        /// holds a value already or one some class part of the instance
        /// declares (<see cref="ClassDefinition.Fields"/>). A part whose class
        /// has no known field list (host-registered) allows every name.
        /// Otherwise raises NameError <c>'x' is not a field of class 'C'</c>
        /// (the compiler's message for <c>@x</c>, A.18a) and returns false.
        /// Only called on an inline-cache miss: a cached property is a
        /// constructor parameter.
        /// </summary>
        private bool CheckDeclared(ClassData cd, string name)
        {
            if (cd.MemoryScope.HasLocal(name) || Declares(cd.Definition, name)) return true;
            foreach (var part in cd.Relations.Values)
                if (Declares(part.Definition, name)) return true;
            RuntimeError(ScriptErrorKind.NameError, $"'{name}' is not a field of class '{cd.Definition.ClassDetails.Name}'");
            return false;

            static bool Declares(ClassDefinition definition, string field) =>
                definition.Fields == null || definition.Fields.Contains(field);
        }

        // ── Import ──
        /// <summary>
        /// Starts an import's top-level code (once per instance) as a
        /// <see cref="FrameKind.Import"/> frame on this VM, above frame
        /// <paramref name="fi"/>, which executes the import (T3.7: no VM of
        /// its own). True when that frame was pushed; false when there is
        /// nothing to run (already imported) or an error was raised. The
        /// import is the one <c>Compile</c> resolved when there is one, whose
        /// definitions are already the script's (one namespace, 5.8); an
        /// import of loaded bytecode is resolved and compiled here, and its
        /// definitions are copied into the definitions of frame
        /// <paramref name="fi"/> and into the script's once its code has run
        /// (<see cref="FinishImport"/>).
        /// </summary>
        private bool ExecuteImport(string path, int fi)
        {
            var callerScope = _frames[fi].Definitions;
            if (_script.ImportStack.Contains(path)) { RuntimeError(ScriptErrorKind.NameError, $"Circular import: '{path}'"); return false; }
            var inNamespace = _script.ResolvedImports.TryGetValue(path, out var resolved);
            if (_script.ImportCache.TryGetValue(path, out var cached))
            {
                if (!inNamespace) JoinNamespace(cached, callerScope);
                return false;
            }

            // Resolved and compiled by Compile() (T3.7); otherwise (an import
            // of loaded bytecode) precompiled first: a host that ships
            // bytecode for this path has already done the lex/parse/compile
            // work this would otherwise repeat at every load. Null means
            // there is none to use.
            CompiledFunction? compiled;
            DefinitionScope? importDefScope;
            if (inNamespace)
            {
                compiled = resolved.TopLevel;
                importDefScope = resolved.Definitions;
            }
            else compiled = _script.LoadImportedBytecode(path, callerScope, out importDefScope);

            if (compiled == null)
            {
                if (_script.FileResolver == null) { RuntimeError(ScriptErrorKind.NameError, $"Cannot import '{path}': no file resolver"); return false; }
                string? source;
                try { source = _script.FileResolver(path); }
                catch (System.Exception e) { RuntimeError(ScriptErrorKind.NameError, $"Failed to read import '{path}': {e.Message}"); return false; }
                if (source == null) { RuntimeError(ScriptErrorKind.NameError, $"Import '{path}' not found"); return false; }

                importDefScope = new DefinitionScope(_script, callerScope);
                _script.ImportStack.Add(path);
                _script.DefinitionContext.PushScope(importDefScope);
                try
                {
                    var tokens = LexicalParser.Parse(source);
                    var module = StatementParser.ParseModule(_script, tokens, path);
                    compiled = Compiler.CompileScript(_script, module.TopLevel, importDefScope);
                }
                catch (SyntaxException e)
                {
                    // Its position is in the imported file (T3.1).
                    e.LocateIn(path, source);
                    throw;
                }
                finally
                {
                    _script.DefinitionContext.EndScope();
                    _script.ImportStack.Remove(path);
                }
            }

            if (!StackFits(_sp, compiled)) return false;
            if (_frameCount >= FramesMax)
            {
                RuntimeError(ScriptErrorKind.LimitExceeded, "Stack overflow");
                return false;
            }

            _runningImports ??= new List<RunningImport>();
            _runningImports.Add(new RunningImport
            {
                Path = path,
                Scope = importDefScope!,
                CallerScope = callerScope,
                InNamespace = inNamespace
            });
            _script.ImportStack.Add(path);

            var stackBase = EnterArguments(0, compiled);
            _frames[_frameCount] = new CallFrame
            {
                Function = compiled,
                IP = 0,
                StackBase = stackBase,
                Kind = FrameKind.Import,
                Definitions = importDefScope!
            };
            _frameCount++;
            return true;
        }

        /// <summary>
        /// The innermost import's frame returned: the import has run, so it
        /// is cached (it runs once per instance), and an import resolved at
        /// run time joins the namespace.
        /// </summary>
        private void FinishImport()
        {
            var import = PopRunningImport();
            _script.ImportCache[import.Path] = import.Scope;
            if (!import.InNamespace) JoinNamespace(import.Scope, import.CallerScope);
        }

        /// <summary>
        /// The innermost import's frame was unwound by an error: the import
        /// has not run, and a later <c>import</c> of it starts again.
        /// </summary>
        private void AbandonImport() => PopRunningImport();

        private RunningImport PopRunningImport()
        {
            var last = _runningImports!.Count - 1;
            var import = _runningImports[last];
            _runningImports.RemoveAt(last);
            _script.ImportStack.Remove(import.Path);
            return import;
        }

        /// <summary>
        /// Every import running in this VM is abandoned (the VM is reset: a
        /// host fault, or a new entry after one that yielded inside an
        /// import's top-level code).
        /// </summary>
        private void AbandonRunningImports()
        {
            if (_runningImports == null) return;
            while (_runningImports.Count > 0) AbandonImport();
        }

        /// <summary>
        /// The definitions of an import resolved at run time join the
        /// importer's and the script's.
        /// </summary>
        private void JoinNamespace(DefinitionScope imported, DefinitionScope callerScope)
        {
            imported.CopyImportedDefinitionsTo(callerScope);
            if (callerScope != _script.Definitions)
                imported.CopyImportedDefinitionsTo(_script.Definitions);
        }
    }
}
