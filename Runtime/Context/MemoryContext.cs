using System.Collections.Generic;

namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="MemoryScope"/> with isolated block of code
    /// </summary>
    public class MemoryContext
    {
        private readonly Stack<MemoryScope> _scopes = new();

        private readonly WarScriptLanguage _script;

        // ── Object pool for short-lived scopes ──
        // Avoids allocating a new MemoryScope + Dictionary on every
        // loop iteration, if-block, and function call.
        private readonly Stack<MemoryScope> _pool = new();
        private const int MaxPoolSize = 64;

        public MemoryContext(WarScriptLanguage script)
        {
            _script = script;
        }
        
        /// <summary>
        /// Get scope of the current block
        /// </summary>
        public MemoryScope GetScope()
        {
            return _scopes.Peek();
        }

        /// <summary>
        /// Create a MemoryScope to enter a nested block.
        /// Reuses a pooled scope if available; allocates only if the pool is empty.
        /// </summary>
        public MemoryScope NewScope()
        {
            var parent = _scopes.Count == 0 ? null : _scopes.Peek();
            return NewScope(parent);
        }

        /// <summary>
        /// Create a MemoryScope with an explicit parent.
        /// Used by standalone function calls (Bug 6 fix) to parent
        /// to the user scope rather than the caller's scope.
        /// </summary>
        public MemoryScope NewScope(MemoryScope parent)
        {
            if (_pool.Count > 0)
            {
                var scope = _pool.Pop();
                scope.Reset(parent);
                return scope;
            }
            return new MemoryScope(_script, parent);
        }

        /// <summary>
        /// Set an existing scope to enter any block
        /// </summary>
        public void PushScope(MemoryScope scope)
        {
            _scopes.Push(scope);
        }
        
        /// <summary>
        /// Terminate the current scope to exit block.
        /// Returns the scope to the pool for reuse if it's poolable and there's room.
        /// </summary>
        public void EndScope()
        {
            var scope = _scopes.Pop();
            if (scope.Poolable && _pool.Count < MaxPoolSize)
                _pool.Push(scope);
        }
    }
}