#nullable enable

using WarScript.Bytecode;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;

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

        // Yield timing
        private YieldType _yieldType;
        private double _waitRemaining;

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
            _coroutineScope = script.MemoryContext.NewScope(memoryScope);
            _coroutineScope.Poolable = false;
        }

        public bool IsReady(double dt)
        {
            switch (_yieldType)
            {
                case YieldType.NextTick: return true;
                case YieldType.Wait:
                    _waitRemaining -= dt;
                    return _waitRemaining <= 0;
                default: return true;
            }
        }

        public void Resume()
        {
            if (IsComplete) return;

            // Push scopes so the VM's SetGlobal writes to our coroutine scope
            _script.DefinitionContext.PushScope(_definitionScope);
            _script.MemoryContext.PushScope(_memoryScope);
            _script.MemoryContext.PushScope(_coroutineScope);
            _script.ClearYield();

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
            finally
            {
                _script.MemoryContext.EndScope();  // coroutine scope
                _script.MemoryContext.EndScope();  // user memory scope
                _script.DefinitionContext.EndScope();

                if (_script.ExceptionContext.IsRaised())
                    _script.ExceptionContext.PrintStackTrace();

                _script.HaltFlags = WarScriptLanguage.HaltFlag.None;
            }

            // Determine next state
            if (_vm.IsYielded)
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
                    _waitRemaining = 0;
                }
                else
                {
                    IsComplete = true;
                }
            }
        }
    }
}
