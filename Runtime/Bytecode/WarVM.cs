#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Stack-based bytecode virtual machine for WarScript.
    /// Replaces tree-walk execution with a tight dispatch loop.
    /// </summary>
    public class WarVM
    {
        private const int StackMax = 1024;
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

        // Pending exception: when an ensure-only handler runs during unwinding,
        // the exception is stashed here and re-raised after ensure completes.
        private WarValue _pendingException;
        private bool _hasPendingException;
        private int _pendingEndIP;

        // Pending return: when Return fires inside a begin/ensure block,
        // the return value is stashed and the ensure block runs first.
        private WarValue _pendingReturnValue;
        private bool _hasPendingReturn;
        private int _pendingReturnEndIP;

        // Scope depth tracking: PushScope increments, PopScope decrements.
        // TryHandler saves it so DoHandleException can restore on unwind.
        private int _scopeDepth;

        // Instruction budgeting: decremented on every dispatch.
        // When it hits 0, the VM raises an exception and stops.
        // A value of 0 after Reset() means unlimited (no budgeting).
        private int _budget;

        // ── Debugger state ──
        // Only accessed when _script.DebugHook != null (zero cost otherwise).
        private StepMode _stepMode;
        private int _stepFrameDepth;
        private int _lastDebugLine;

        // ── Memory budgeting ──
        // Tracks heap allocations (strings, arrays, class instances).
        // 0 = unlimited. Raises "Memory budget exceeded" when hit.
        private long _memoryUsed;
        private long _memoryBudget;

        // ── Coroutine support ──
        // When the VM is used as a coroutine executor, yield opcodes
        // set these fields and return from Execute(). The coroutine
        // wrapper reads them to determine how to wait.
        private bool _yielded;
        private YieldType _yieldType;
        private double _yieldWaitDuration;

        /// <summary>True if the last Execute() stopped because of a yield opcode.</summary>
        public bool IsYielded => _yielded;

        /// <summary>The type of yield (NextTick, Wait, Until).</summary>
        public YieldType SuspendedYieldType => _yieldType;

        /// <summary>For YieldWait: the requested wait duration in seconds.</summary>
        public double SuspendedWaitDuration => _yieldWaitDuration;

        /// <summary>True if the VM has finished executing (no more frames).</summary>
        public bool IsCompleted => _frameCount == 0 && !_yielded;

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

        public WarVM(WarScriptLanguage script)
        {
            _script = script;
            _interner = script.Strings;
        }

        private readonly StringInterner _interner;

        /// <summary>
        /// Reset all VM state for reuse. Avoids re-allocating the stack,
        /// frame, and handler arrays on every Call() invocation.
        /// </summary>
        private void Reset()
        {
            _sp = 0;
            _frameCount = 0;
            _handlerCount = 0;
            _topLevelResult = default;
            _hasPendingException = false;
            _hasPendingReturn = false;
            _scopeDepth = 0;
            _budget = _script.InstructionBudget;
            _yielded = false;
            _yieldType = YieldType.NextTick;
            _yieldWaitDuration = 0;
            _stepMode = StepMode.Continue;
            _stepFrameDepth = 0;
            _lastDebugLine = -1;
            _memoryUsed = 0;
            _memoryBudget = _script.MemoryBudget;
        }

        public void Run(CompiledFunction main)
        {
            Reset();

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
            _yielded = false;
            _yieldType = YieldType.NextTick;
            _yieldWaitDuration = 0;
            _budget = _script.InstructionBudget;
            _memoryUsed = 0;
            _memoryBudget = _script.MemoryBudget;
            Execute();
        }

        // ────────────────────────────────────────────────────────
        //  Main dispatch loop
        // ────────────────────────────────────────────────────────

        private void Execute()
        {
            // These are reloaded from the _frames array whenever the current
            // frame changes (after Call or Return).
            int fi = _frameCount - 1;
            List<byte> code = _frames[fi].Function.Chunk.Code;
            List<WarValue> constants = _frames[fi].Function.Chunk.Constants;

            while (true)
            {
                // ── Instruction budget ──
                if (_budget > 0 && --_budget == 0)
                {
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
                    var lines = _frames[fi].Function.Chunk.Lines;
                    var ip = _frames[fi].IP;
                    if (ip < lines.Count)
                    {
                        var currentLine = lines[ip];
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
                        Push(_script.MemoryContext.GetScope().Get(name));
                        break;
                    }
                    case OpCode.SetGlobal:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var name = constants[nameIdx].TextValue;
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
                            var arr = AddArrays(a, b);
                            TrackAlloc(EstimateArrayBytes(arr.ArrayValue.Count));
                            Push(arr);
                        }
                        else
                        {
                            var s = _interner.Intern(a.ToString() + b.ToString());
                            TrackAlloc(EstimateStringBytes(s));
                            Push(WarValue.FromText(s));
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
                            var s = _interner.Intern(a.ToString().Replace(b.ToString(), ""));
                            TrackAlloc(EstimateStringBytes(s));
                            Push(WarValue.FromText(s));
                        }
                        break;
                    }
                    case OpCode.Mul:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNumeric && b.IsNumeric)
                            Push(WarValue.FromNumeric(a.Numeric * b.Numeric));
                        else if (a.IsText && b.IsNumeric)
                        {
                            var s = _interner.Intern(WarValue.RepeatString(a.TextValue, (int)b.Numeric));
                            TrackAlloc(EstimateStringBytes(s));
                            Push(WarValue.FromText(s));
                        }
                        else if (b.IsText && a.IsNumeric)
                        {
                            var s = _interner.Intern(WarValue.RepeatString(b.TextValue, (int)a.Numeric));
                            TrackAlloc(EstimateStringBytes(s));
                            Push(WarValue.FromText(s));
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
                            Push(WarValue.FromNumeric(a.Numeric / b.Numeric));
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
                        Push(WarValue.FromNumeric(a.Numeric % b.Numeric));
                        break;
                    }
                    case OpCode.Negate:
                        Push(WarValue.FromNumeric(-Pop().Numeric));
                        break;

                    // ══════════════════════════════════════════════
                    //  Comparison
                    // ══════════════════════════════════════════════

                    case OpCode.Equal:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNull || b.IsNull)
                            Push(WarValue.FromLogical(a.IsNull && b.IsNull));
                        else if (a.Tag == b.Tag)
                            Push(WarValue.FromLogical(a.Equals(b)));
                        else
                            Push(WarValue.FromLogical(a.ToString() == b.ToString()));
                        break;
                    }
                    case OpCode.NotEqual:
                    {
                        var b = Pop(); var a = Pop();
                        if (a.IsNull || b.IsNull)
                            Push(WarValue.FromLogical(!(a.IsNull && b.IsNull)));
                        else if (a.Tag == b.Tag)
                            Push(WarValue.FromLogical(!a.Equals(b)));
                        else
                            Push(WarValue.FromLogical(a.ToString() != b.ToString()));
                        break;
                    }
                    case OpCode.Less:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(a.CompareTo(b) < 0));
                        break;
                    }
                    case OpCode.LessEqual:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(a.CompareTo(b) <= 0));
                        break;
                    }
                    case OpCode.Greater:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(a.CompareTo(b) > 0));
                        break;
                    }
                    case OpCode.GreaterEqual:
                    {
                        var b = Pop(); var a = Pop();
                        Push(WarValue.FromLogical(a.CompareTo(b) >= 0));
                        break;
                    }

                    // ── Superinstructions: Compare + JumpIfFalse + Pop ──
                    // Fuses three dispatches into one. Pops two operands,
                    // compares, and jumps if the condition is false.
                    // No intermediate boolean is pushed.

                    case OpCode.LessJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!(a.CompareTo(b) < 0))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.LessEqualJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!(a.CompareTo(b) <= 0))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.GreaterJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!(a.CompareTo(b) > 0))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.GreaterEqualJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!(a.CompareTo(b) >= 0))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.EqualJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (!a.Equals(b))
                            _frames[fi].IP += offset;
                        break;
                    }
                    case OpCode.NotEqualJump:
                    {
                        var offset = ReadU16(code, ref _frames[fi].IP);
                        var b = Pop(); var a = Pop();
                        if (a.Equals(b))
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
                        if (def == null)
                        {
                            RuntimeError($"Function '{funcName}' with {argCount} args is not defined");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        if (def is NativeFunctionDefinition nativeFn)
                        {
                            var args = CollectArgs(argCount);
                            WarValue result;
                            try { result = nativeFn.NativeBody(args); }
                            catch (System.Exception e)
                            {
                                RuntimeError($"Native function '{funcName}' failed: {e.Message}");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(result);
                        }
                        else if (def.Compiled != null)
                        {
                            // Pad missing arguments with null for default parameters.
                            // The function body's desugared null-checks will assign defaults.
                            var arity = def.Compiled.Arity;
                            while (argCount < arity)
                            {
                                Push(WarValue.Null);
                                argCount++;
                            }

                            // Push a MemoryScope for function-local variables
                            // (parented to UserMemoryScope, matching tree-walker semantics)
                            _script.MemoryContext.PushScope(
                                _script.MemoryContext.NewScope(_script.UserMemoryScope));

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
                                HasScope = true,
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

                    case OpCode.TailCall:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var argCount = code[_frames[fi].IP++];
                        var funcName = constants[nameIdx].TextValue;

                        var def = _script.DefinitionContext.GetScope().GetFunction(funcName, argCount);
                        if (def == null)
                        {
                            RuntimeError($"Function '{funcName}' with {argCount} args is not defined");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        if (def is NativeFunctionDefinition nativeTail)
                        {
                            // Can't tail-call into native — call normally and return the result
                            var args = CollectArgs(argCount);
                            WarValue nResult;
                            try { nResult = nativeTail.NativeBody(args); }
                            catch (System.Exception e)
                            {
                                RuntimeError($"Native function '{funcName}' failed: {e.Message}");
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            // Behave as Return with this result
                            // Pop excess scopes
                            while (_scopeDepth > _frames[fi].SavedScopeDepth)
                            {
                                _script.MemoryContext.EndScope();
                                _scopeDepth--;
                            }
                            DoReturn(nResult, fi, ref code, ref constants, out fi);
                            if (_frameCount == 0) return;
                            code = _frames[fi].Function.Chunk.Code;
                            constants = _frames[fi].Function.Chunk.Constants;
                        }
                        else if (def.Compiled != null)
                        {
                            // ── True tail call: reuse the current frame ──

                            // Pad missing arguments with null for default parameters.
                            var arity = def.Compiled.Arity;
                            while (argCount < arity)
                            {
                                Push(WarValue.Null);
                                argCount++;
                            }

                            // Pop old function's memory scope
                            if (_frames[fi].HasScope)
                                _script.MemoryContext.EndScope();

                            // Push fresh scope for the new function
                            _script.MemoryContext.PushScope(
                                _script.MemoryContext.NewScope(_script.UserMemoryScope));

                            // Move arguments down to the frame's stack base
                            var stackBase = _frames[fi].StackBase;
                            var argBase = _sp - argCount;
                            for (int i = 0; i < argCount; i++)
                                _stack[stackBase + i] = _stack[argBase + i];
                            _sp = stackBase + argCount;

                            // Reuse the frame — no _frameCount change
                            _frames[fi].Function = def.Compiled;
                            _frames[fi].IP = 0;
                            _frames[fi].HasScope = true;
                            code = def.Compiled.Chunk.Code;
                            constants = def.Compiled.Chunk.Constants;
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

                        // Check if there are any handlers on this frame with ensure blocks.
                        while (_handlerCount > 0 && _handlers[_handlerCount - 1].FrameIndex == fi)
                        {
                            var handler = _handlers[--_handlerCount];
                            while (_scopeDepth > handler.SavedScopeDepth)
                            {
                                _script.MemoryContext.EndScope();
                                _scopeDepth--;
                            }
                            if (handler.EnsureIP != handler.EndIP)
                            {
                                _sp = handler.SavedSP;
                                _hasPendingReturn = true;
                                _pendingReturnValue = result;
                                _pendingReturnEndIP = handler.EndIP;
                                _frames[fi].IP = handler.EnsureIP;
                                goto nextInstruction;
                            }
                        }

                        // Pop any remaining excess scopes (e.g. from rescue body PushScope
                        // where the handler was already consumed during exception handling)
                        while (_scopeDepth > _frames[fi].SavedScopeDepth)
                        {
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
                            RuntimeError($"Method '{methodName}' with {argCount} args not found");
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }

                        var methodDef = classDef.GetDefinitionScope().GetFunction(methodName, argCount)!;
                        var methodClassData = classData.GetRelation(classDef.ClassDetails.Name) ?? classData;

                        // Remove instance from under the args
                        var argBase = _sp - argCount - 1;
                        for (int i = 0; i < argCount; i++)
                            _stack[argBase + i] = _stack[argBase + i + 1];
                        _sp--;

                        if (methodDef is NativeFunctionDefinition nativeMethod)
                        {
                            var args = CollectArgs(argCount);
                            Push(nativeMethod.NativeBody(args));
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
                                _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

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
                                    IsMethodCall = true,
                                    HasScope = true,
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
                        var list = new List<WarValue>(count);
                        var arrBase = _sp - count;
                        for (int i = 0; i < count; i++)
                            list.Add(_stack[arrBase + i]);
                        _sp = arrBase;
                        TrackAlloc(EstimateArrayBytes(count));
                        Push(WarValue.FromArray(list));
                        break;
                    }
                    case OpCode.IndexGet:
                    {
                        var index = Pop(); var target = Pop();
                        if (target.IsArray && index.IsNumeric)
                            Push(target.GetArrayElement((int)index.Numeric));
                        else if (target.IsText && index.IsNumeric)
                            Push(target.GetTextChar((int)index.Numeric));
                        else
                            Push(WarValue.Null);
                        break;
                    }
                    case OpCode.IndexSet:
                    {
                        var value = Pop(); var index = Pop(); var target = Pop();
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement((int)index.Numeric, value);
                        Push(value);
                        break;
                    }
                    case OpCode.IndexSetLocal:
                    {
                        // Stack: [..., index, value]. Operand: local slot.
                        var slot = ReadU16(code, ref _frames[fi].IP);
                        var value = Pop(); var index = Pop();
                        var target = _stack[_frames[fi].StackBase + slot];
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement((int)index.Numeric, value);
                        else if (target.IsText && index.IsNumeric)
                        {
                            var newText = target.SetTextChar((int)index.Numeric, value.ToString());
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
                        if (target.IsArray && index.IsNumeric)
                            target.SetArrayElement((int)index.Numeric, value);
                        else if (target.IsText && index.IsNumeric)
                        {
                            var newText = target.SetTextChar((int)index.Numeric, value.ToString());
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

                            if (propVal.IsArray && index.IsNumeric)
                                propVal.SetArrayElement((int)index.Numeric, value);
                            else if (propVal.IsText && index.IsNumeric)
                            {
                                var newText = propVal.SetTextChar((int)index.Numeric, value.ToString());
                                if (ReferenceEquals(cache.CachedType, details))
                                    cd.SetPropertyByIndex(cache.CachedIndex, newText);
                                else
                                    cd.SetProperty(constants[nameIdx].TextValue, newText);
                            }
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.ArrayAppend:
                    {
                        var value = Pop(); var arr = Peek();
                        if (arr.IsArray)
                        {
                            TrackAlloc(16); // one WarValue slot
                            arr.ArrayAppend(value);
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
                            var list = new List<WarValue>(props.Count);
                            for (int i = 0; i < props.Count; i++)
                                list.Add(cd.GetProperty(props[i]));
                            TrackAlloc(EstimateArrayBytes(props.Count));
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
                        if (_script.ExceptionContext.IsRaised())
                        {
                            if (DoHandleException(ref fi, ref code, ref constants)) break;
                            return;
                        }
                        Push(result);
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

                        // Push parent's definition scope so the nested class can be found
                        _script.DefinitionContext.PushScope(parent.ClassValue.Definition.GetDefinitionScope());
                        try
                        {
                            // Re-push args for InstantiateClass
                            foreach (var arg in args) Push(arg);
                            var result = InstantiateClass(className, argCount);
                            if (_script.ExceptionContext.IsRaised())
                            {
                                if (DoHandleException(ref fi, ref code, ref constants)) break;
                                return;
                            }
                            Push(result);
                        }
                        finally
                        {
                            _script.DefinitionContext.EndScope();
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
                                cd.SetProperty(propName, value);
                                if (details.PropertyIndex.TryGetValue(propName, out var idx))
                                {
                                    cache.CachedType = details;
                                    cache.CachedIndex = idx;
                                }
                            }
                        }
                        Push(value);
                        break;
                    }
                    case OpCode.This:
                        Push(WarValue.FromClass(_script.ClassInstanceContext.GetValue()));
                        break;

                    // ── Superinstructions: This + GetProperty / SetProperty ──
                    case OpCode.ThisGetProperty:
                    {
                        var nameIdx = ReadU16(code, ref _frames[fi].IP);
                        var cacheSlot = ReadU16(code, ref _frames[fi].IP);
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
                        _script.Logger?.Invoke(_script, Pop().ToString());
                        break;

                    case OpCode.Assert:
                    {
                        if (!IsTruthy(Pop()))
                        {
                            var line = _frames[fi].IP > 0 && _frames[fi].IP - 1 < _frames[fi].Function.Chunk.Lines.Count
                                ? _frames[fi].Function.Chunk.Lines[_frames[fi].IP - 1] : 0;
                            RuntimeError($"Assertion error at line {line}");
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
                        _yieldWaitDuration = 0;
                        _script.SetYielded(YieldType.NextTick, 0);
                        return;
                    case OpCode.YieldWait:
                    {
                        var dur = Pop();
                        var d = dur.IsNumeric ? dur.Numeric : 0;
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
                }

                // Check for pending exception (after ensure-only block completes)
                if (_hasPendingException && _frames[fi].IP >= _pendingEndIP)
                {
                    _hasPendingException = false;
                    _script.ExceptionContext.Enable();
                    if (DoHandleException(ref fi, ref code, ref constants))
                        continue;
                    return;
                }

                // Check for pending return (after ensure block runs before return)
                if (_hasPendingReturn && _frames[fi].IP >= _pendingReturnEndIP)
                {
                    _hasPendingReturn = false;
                    var result = _pendingReturnValue;
                    _pendingReturnValue = default;

                    // Check for more handlers on this frame
                    while (_handlerCount > 0 && _handlers[_handlerCount - 1].FrameIndex == fi)
                    {
                        var h = _handlers[--_handlerCount];
                        while (_scopeDepth > h.SavedScopeDepth)
                        {
                            _script.MemoryContext.EndScope();
                            _scopeDepth--;
                        }
                        if (h.EnsureIP != h.EndIP)
                        {
                            _sp = h.SavedSP;
                            _hasPendingReturn = true;
                            _pendingReturnValue = result;
                            _pendingReturnEndIP = h.EndIP;
                            _frames[fi].IP = h.EnsureIP;
                            goto continueLoop;
                        }
                    }

                    // Pop excess scopes before returning
                    while (_scopeDepth > _frames[fi].SavedScopeDepth)
                    {
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
            var wasMethod = _frames[fi].IsMethodCall;
            var hasScope = _frames[fi].HasScope;

            _sp = _frames[fi].StackBase;
            _frameCount--;

            if (wasMethod)
            {
                _script.DefinitionContext.EndScope();
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }
            if (hasScope)
                _script.MemoryContext.EndScope();

            if (_frameCount > 0)
                Push(result);
            else
                _topLevelResult = result;

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
                var frameLine = frameIP < frame.Function.Chunk.Lines.Count
                    ? frame.Function.Chunk.Lines[frameIP] : 0;
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
                if (name == null || name.StartsWith("$")) continue;
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

        private void RuntimeError(string message)
        {
            _script.ExceptionContext.RaiseException(message);
        }

        /// <summary>
        /// Track a heap allocation. Returns true if within budget.
        /// If over budget, raises "Memory budget exceeded" and returns false.
        /// Caller must check the return value and handle the exception.
        /// </summary>
        private bool TrackAlloc(long bytes)
        {
            if (_memoryBudget <= 0) return true;
            _memoryUsed += bytes;
            if (_memoryUsed <= _memoryBudget) return true;
            RuntimeError("Memory budget exceeded");
            return false;
        }

        /// <summary>Estimate heap cost of a string (UTF-16 + object overhead).</summary>
        private static long EstimateStringBytes(string s) => s.Length * 2L + 40;

        /// <summary>Estimate heap cost of a new array.</summary>
        private static long EstimateArrayBytes(int count) => count * 16L + 64;

        /// <summary>Estimate heap cost of a class instance.</summary>
        private static long EstimateClassBytes(int propertyCount) => propertyCount * 16L + 96;

        private bool DoHandleException(ref int fi, ref List<byte> code, ref List<WarValue> constants)
        {
            while (_handlerCount > 0)
            {
                var handler = _handlers[_handlerCount - 1];

                if (handler.FrameIndex != fi)
                {
                    // Unwind this frame — pop excess scopes first
                    while (_scopeDepth > _frames[fi].SavedScopeDepth)
                    {
                        _script.MemoryContext.EndScope();
                        _scopeDepth--;
                    }
                    var wasMethod = _frames[fi].IsMethodCall;
                    var hasScope = _frames[fi].HasScope;
                    _sp = _frames[fi].StackBase;
                    _frameCount--;
                    if (wasMethod)
                    {
                        _script.DefinitionContext.EndScope();
                        _script.MemoryContext.EndScope();
                        _script.ClassInstanceContext.PopValue();
                    }
                    if (hasScope)
                        _script.MemoryContext.EndScope();
                    if (_frameCount == 0) return false;
                    fi = _frameCount - 1;
                    code = _frames[fi].Function.Chunk.Code;
                    constants = _frames[fi].Function.Chunk.Constants;
                    continue;
                }

                _handlerCount--;
                var errorVal = _script.ExceptionContext.Exception?.Value ?? WarValue.Null;
                _sp = handler.SavedSP;

                // Pop any MemoryContext scopes pushed since the handler was installed
                while (_scopeDepth > handler.SavedScopeDepth)
                {
                    _script.MemoryContext.EndScope();
                    _scopeDepth--;
                }

                if (handler.HasRescue)
                {
                    // Has rescue: clear exception, push error, jump to rescue.
                    // Rescue code falls through to ensure.
                    _script.ExceptionContext.RescueException();
                    Push(errorVal);
                    _frames[fi].IP = handler.RescueIP;
                    return true;
                }
                else
                {
                    // No rescue, but ensure must run.
                    // Temporarily disable the exception, stash it, run ensure.
                    // When ensure finishes (IP reaches endIP), re-raise.
                    _script.ExceptionContext.Disable();
                    _hasPendingException = true;
                    _pendingException = errorVal;
                    _pendingEndIP = handler.EndIP;
                    _frames[fi].IP = handler.EnsureIP;
                    return true;
                }
            }
            return false;
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

        // ── Class instantiation ──
        private WarValue InstantiateClass(string className, int argCount)
        {
            var args = CollectArgs(argCount);
            var definition = _script.DefinitionContext.GetScope().GetClass(className);
            if (definition == null)
            {
                RuntimeError($"Class '{className}' is not defined");
                return default;
            }
            return InstantiateClassDef(definition, args, new Dictionary<string, ClassData>());
        }

        private WarValue InstantiateClassDef(ClassDefinition definition, List<WarValue> args,
            Dictionary<string, ClassData> relations)
        {
            return InstantiateClassDefShared(definition, args, relations, null);
        }

        /// <summary>
        /// Instantiate a class, optionally sharing ValueReferences from a derived class.
        /// When sharedRefs is non-null, property slots map to the derived class's refs.
        /// </summary>
        private WarValue InstantiateClassDefShared(ClassDefinition definition, List<WarValue> args,
            Dictionary<string, ClassData> relations, ValueReference[]? sharedRefs)
        {
            var propCount = definition.ClassDetails.Properties.Count;
            TrackAlloc(EstimateClassBytes(propCount));

            var valueRefs = new ValueReference[propCount];
            for (int i = 0; i < propCount; i++)
            {
                if (sharedRefs != null && i < sharedRefs.Length && sharedRefs[i] != null)
                    valueRefs[i] = sharedRefs[i]; // share the same reference
                else
                    valueRefs[i] = ValueReference.InstanceOf(i < args.Count ? args[i] : WarValue.Null);
            }

            var classScope = new MemoryScope(_script, null, poolable: false);
            var classData = new ClassData(definition, classScope, relations);
            classData.PropertyValues = valueRefs;
            relations[definition.ClassDetails.Name] = classData;

            // Base classes: pass shared ValueReferences
            foreach (var baseType in definition.BaseTypes)
            {
                var baseDef = _script.DefinitionContext.GetScope().GetClass(baseType.Name);
                if (baseDef == null) continue;

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

                InstantiateClassDefShared(baseDef, baseArgs, relations, baseShared);
            }

            _script.MemoryContext.PushScope(classScope);
            _script.ClassInstanceContext.PushValue(classData);
            try
            {
                for (int i = 0; i < propCount; i++)
                    classScope.SetLocal(definition.ClassDetails.Properties[i], valueRefs[i]);

                // Execute constructor
                if (definition.CompiledConstructor != null)
                {
                    _script.DefinitionContext.PushScope(definition.GetDefinitionScope());
                    try { new WarVM(_script).Run(definition.CompiledConstructor); }
                    finally { _script.DefinitionContext.EndScope(); }
                }
                // No tree-walk fallback — AST is discarded after compilation.
                // Classes loaded via LoadBytecode have CompiledConstructor or nothing.
            }
            finally
            {
                _script.MemoryContext.EndScope();
                _script.ClassInstanceContext.PopValue();
            }

            return WarValue.FromClass(classData);
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

        // ── Import ──
        private void ExecuteImport(string path)
        {
            if (_script.FileResolver == null) { RuntimeError($"Cannot import '{path}': no file resolver"); return; }
            if (_script.ImportStack.Contains(path)) { RuntimeError($"Circular import: '{path}'"); return; }
            if (_script.ImportCache.TryGetValue(path, out var cached))
            {
                cached.CopyLocalDefinitionsTo(_script.DefinitionContext.GetScope());
                return;
            }

            string? source;
            try { source = _script.FileResolver(path); }
            catch (System.Exception e) { RuntimeError($"Failed to read import '{path}': {e.Message}"); return; }
            if (source == null) { RuntimeError($"Import '{path}' not found"); return; }

            _script.ImportStack.Add(path);
            var callerScope = _script.DefinitionContext.GetScope();
            var importDefScope = _script.DefinitionContext.NewScope();
            _script.DefinitionContext.PushScope(importDefScope);
            try
            {
                var tokens = LexicalParser.Parse(source);
                var importStmt = new CompositeStatement(_script, null, path);
                StatementParser.Parse(_script, tokens, importStmt);
                var compiled = Compiler.CompileScript(_script, importStmt, importDefScope);
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
