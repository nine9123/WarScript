using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Associates a given <see cref="DefinitionScope"/> with isolated block of code
    ///
    /// <see cref="DefinitionScope"/>
    /// <see cref="ClassDefinition"/>
    /// <see cref="FunctionDefinition"/>
    /// </summary>
    public class DefinitionContext
    {
        private readonly Stack<DefinitionScope> _scopes = new();

        private readonly WarScriptLanguage _script;
        
        public DefinitionContext(WarScriptLanguage script)
        {
            _script = script;
        }
        
        /// <summary>
        /// Get scope of the current block
        /// </summary>
        /// <returns></returns>
        public DefinitionScope GetScope()
        {
            return _scopes.Peek();
        }

        /// <summary>
        /// Create and set a new DefinitionScope to enter a nested block
        /// </summary>
        /// <returns></returns>
        public DefinitionScope NewScope()
        {
            return new DefinitionScope(_script, _scopes.Count == 0 ? null : _scopes.Peek());
        }

        /// <summary>
        /// Set an existing scope to enter any block
        /// </summary>
        public void PushScope(DefinitionScope scope)
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