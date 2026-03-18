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
        /// Create and set a new MemoryScope to enter a nested block
        /// </summary>
        public MemoryScope NewScope()
        {
            return new MemoryScope(_script, _scopes.Count == 0 ? null : _scopes.Peek());
        }

        /// <summary>
        /// Set an existing scope to enter any block
        /// </summary>
        public void PushScope(MemoryScope scope)
        {
            _scopes.Push(scope);
        }
        
        /// <summary>
        /// Terminate the current scope to exit block
        /// </summary>
        public void EndScope()
        {
            _scopes.Pop();
        }
    }
}