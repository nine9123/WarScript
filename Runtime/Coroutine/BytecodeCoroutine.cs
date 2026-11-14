#nullable enable

using System.Collections.Generic;
using WarScript.Bytecode;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;
using FixMath;

namespace WarScript
{
    /// <summary>
    /// A coroutine backed by the bytecode VM. Each instance owns its own
    /// <see cref="WarVM"/> whose full execution state (IP, stack, frames,
    /// handlers) is preserved across yields. This allows yield to work
    /// anywhere — inside loops, if-blocks, and nested function calls.
    /// </summary>
    public class BytecodeCoroutine : ICoroutine
    {
        public int Id { get; }
        public bool IsComplete { get; private set; }

        private readonly WarScriptLanguage _script;
        private readonly CompiledFunction _compiled;
        private readonly DefinitionScope _definitionScope;
        private readonly MemoryScope _memoryScope;
        private readonly WarValue[] _args;
        private readonly bool _loop;

        private readonly WarVM _vm;
        private readonly MemoryScope _coroutineScope;
        private bool _started;
        private bool _faulted;

        // Scopes the VM pushed for calls still in flight when it yielded
        // (function locals, method class context, block scopes). They are set
        // aside between ticks and pushed back on resume, so the VM's frames
        // find them where they left them. Stored top-of-stack first.
        private readonly List<MemoryScope> _suspendedMemory = new();
        private readonly List<DefinitionScope> _suspendedDefinitions = new();
        private readonly List<ClassData> _suspendedInstances = new();

        // Yield timing
        private YieldType _yieldType;
        private F64 _waitRemaining;

        public BytecodeCoroutine(
            WarScriptLanguage script,
            CompiledFunction compiled,
            DefinitionScope definitionScope,
            MemoryScope memoryScope,
            WarValue[] args,
            bool loop,
            int id)
        {
            _script = script;
            _compiled = compiled;
            _definitionScope = definitionScope;
            _memoryScope = memoryScope;
            _args = args;
            _loop = loop;
            Id = id;

            _vm = new WarVM(script);
            _coroutineScope = script.MemoryContext.NewScope(memoryScope, MemoryScopeKind.FunctionLocal);
            _coroutineScope.Poolable = false;
        }

        public bool IsReady(F64 dt)
        {
            switch (_yieldType)
            {
                case YieldType.NextTick: return true;
                case YieldType.Wait:
                    _waitRemaining -= dt;
                    return _waitRemaining <= F64.Zero;
                default: return true;
            }
        }

        /// <summary>
        /// Run the coroutine to its next yield or its end: a host entry.
        /// Returns <c>Ok</c> (null value) when it yielded, completed or did
        /// not run because it is complete or already running; otherwise the
        /// error that ended this run (plan T2.3), which is logged.
        /// </summary>
        public ScriptResult Resume()
        {
            if (IsComplete) return ScriptResult.Ok(WarValue.Null);
            // Resumed from inside itself (a native it called ticked the
            // coroutines): its VM is busy and must not be reset (T1.5).
            if (_vm.IsRunning) return ScriptResult.Ok(WarValue.Null);
            // A host entry (T1.5): the outermost one resets the instance's
            // budgets. Too deeply nested, it does not run and the error
            // stays raised for the native that started or ticked it.
            if (!_script.BeginEntry()) return _script.EntryResult(WarValue.Null);
            ScriptResult result = ScriptResult.Ok(WarValue.Null);

            // Push scopes so the VM's SetGlobal writes to our coroutine scope
            _script.DefinitionContext.PushScope(_definitionScope);
            _script.MemoryContext.PushScope(_memoryScope);
            _script.MemoryContext.PushScope(_coroutineScope);
            _script.ClearYield();

            int memoryBase = _script.MemoryContext.Depth;
            int definitionBase = _script.DefinitionContext.Depth;
            int instanceBase = _script.ClassInstanceContext.Depth;

            for (int i = _suspendedMemory.Count - 1; i >= 0; i--)
                _script.MemoryContext.PushScope(_suspendedMemory[i]);
            for (int i = _suspendedDefinitions.Count - 1; i >= 0; i--)
                _script.DefinitionContext.PushScope(_suspendedDefinitions[i]);
            for (int i = _suspendedInstances.Count - 1; i >= 0; i--)
                _script.ClassInstanceContext.PushValue(_suspendedInstances[i]);
            _suspendedMemory.Clear();
            _suspendedDefinitions.Clear();
            _suspendedInstances.Clear();

            try
            {
                if (!_started)
                {
                    _vm.InitCoroutine(_compiled, _args);
                    _started = true;
                }
                else
                {
                    _vm.ResumeCoroutine();
                }
            }
            catch (Exception.SyntaxException e) when (_script.EntryDepth == 1)
            {
                // An imported file does not compile: the coroutine ends.
                _faulted = true;
                result = _script.ImportCompileFailure(e);
            }
            catch (System.Exception e) when (!(e is Exception.SyntaxException))
            {
                // Host fault: the VM has reset itself, so the coroutine is
                // finished (a looping one too) and the fault is reported.
                _faulted = true;
                result = _script.ReportHostFault(e);
            }
            finally
            {
                // Take off whatever the VM left above our own scopes: keep it
                // for the next resume if suspended, otherwise discard it.
                bool suspended = _vm.IsYielded;
                while (_script.MemoryContext.Depth > memoryBase)
                {
                    if (suspended) _suspendedMemory.Add(_script.MemoryContext.DetachScope());
                    else _script.MemoryContext.EndScope();
                }
                while (_script.DefinitionContext.Depth > definitionBase)
                {
                    var scope = _script.DefinitionContext.DetachScope();
                    if (suspended) _suspendedDefinitions.Add(scope);
                }
                while (_script.ClassInstanceContext.Depth > instanceBase)
                {
                    var instance = _script.ClassInstanceContext.DetachValue();
                    if (suspended) _suspendedInstances.Add(instance);
                }

                _script.MemoryContext.EndScope();  // coroutine scope
                _script.MemoryContext.EndScope();  // user memory scope
                _script.DefinitionContext.EndScope();

                // The coroutine's own unhandled error ends it and is logged,
                // even when the coroutine was started or ticked by a native
                // (v1; not propagated to the caller like a nested Call's).
                if (_script.ExceptionContext.IsRaised())
                {
                    result = _script.EntryResult(WarValue.Null);
                    _script.ExceptionContext.PrintStackTrace();
                }
                _script.EndEntry();
            }

            // Determine next state
            if (_faulted)
            {
                IsComplete = true;
            }
            else if (_vm.IsYielded)
            {
                _yieldType = _vm.SuspendedYieldType;
                _waitRemaining = _vm.SuspendedWaitDuration;
                _script.ClearYield();
            }
            else
            {
                // Function completed (or errored)
                if (_loop)
                {
                    // Restart on next tick
                    _started = false;
                    _yieldType = YieldType.NextTick;
                    _waitRemaining = F64.Zero;
                }
                else
                {
                    IsComplete = true;
                }
            }
            return result;
        }
    }
}
