using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Associates a given {@link DefinitionScope} with isolated block of code
    ///
    /// <see cref="DefinitionScope"/>
    /// <see cref="ClassDefinition"/>
    /// <see cref="FunctionDefinition"/>
    /// </summary>
    public class DefinitionContext
    {
        private static readonly Stack<DefinitionScope> Scopes = new Stack<DefinitionScope>();

        /// <summary>
        /// Get scope of the current block
        /// </summary>
        /// <returns></returns>
        public static DefinitionScope GetScope()
        {
            return Scopes.Peek();
        }

        /// <summary>
        /// Create and set a new DefinitionScope to enter a nested block
        /// </summary>
        /// <returns></returns>
        public static DefinitionScope NewScope()
        {
            return new DefinitionScope(Scopes.Count == 0 ? null : Scopes.Peek());
        }

        /// <summary>
        /// Set an existing scope to enter any block
        /// </summary>
        public static void PushScope(DefinitionScope scope)
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