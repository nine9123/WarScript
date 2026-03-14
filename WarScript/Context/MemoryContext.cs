using System.Collections.Generic;

namespace WarScript.Context
{
    /// <summary>
    /// Associates a given <see cref="MemoryScope"/> with isolated block of code
    /// </summary>
    public class MemoryContext
    {
        private static readonly Stack<MemoryScope> Scopes = new Stack<MemoryScope>();
        
        /// <summary>
        /// Get scope of the current block
        /// </summary>
        public static MemoryScope GetScope()
        {
            return Scopes.Peek();
        }

        /// <summary>
        /// Create and set a new MemoryScope to enter a nested block
        /// </summary>
        public static MemoryScope NewScope()
        {
            return new MemoryScope(Scopes.Count == 0 ? null : Scopes.Peek());
        }

        /// <summary>
        /// Set an existing scope to enter any block
        /// </summary>
        public static void PushScope(MemoryScope scope)
        {
            Scopes.Push(scope);
        }
        
        /// <summary>
        /// Terminate the current scope to exit block
        /// </summary>
        public static void EndScope()
        {
            Scopes.Pop();
        }
    }
}