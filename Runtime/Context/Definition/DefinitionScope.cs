#nullable enable

using System.Collections.Generic;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Contains structures (classes, functions) defined in a block of code
    ///
    /// <see cref="ClassDefinition"/>
    /// <see cref="FunctionDefinition"/>
    /// <see cref="DefinitionContext"/>
    /// </summary>
    public class DefinitionScope
    {
        /// <summary>
        /// Classes defined in the block: lookup by name
        /// </summary>
        private readonly Dictionary<string, ClassDefinition> _classes;

        /// <summary>
        /// All class definitions in this scope (for compiler iteration).
        /// </summary>
        public IEnumerable<ClassDefinition> ClassDefinitions => _classes.Values;

        /// <summary>
        /// Functions declared in the block
        /// </summary>
        public readonly List<FunctionDefinition> Functions;

        /// <summary>
        /// Function lookup
        /// </summary>
        private readonly Dictionary<(string name, int argCount), FunctionDefinition> _functionIndex;

        /// <summary>
        /// Parent DefinitionScope to access the structures defined in outer blocks of code
        /// </summary>
        private readonly DefinitionScope? _parent;

        private readonly WarScriptLanguage _script;
        
        public DefinitionScope(WarScriptLanguage script, DefinitionScope? parent)
        {
            _script = script;
            _classes = new Dictionary<string, ClassDefinition>();
            Functions = new List<FunctionDefinition>();
            _functionIndex = new Dictionary<(string, int), FunctionDefinition>();
            _parent = parent;
        }

        /// <summary>
        /// Get ClassDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the class</param>
        public ClassDefinition? GetClass(string name)
        {
            if (_classes.TryGetValue(name, out var classDefinition))
                return classDefinition;

            return _parent?.GetClass(name);
        }

        /// <summary>
        /// Add ClassDefinition to the current block
        /// </summary>
        public void AddClass(ClassDefinition classDefinition)
        {
            _classes[classDefinition.ClassDetails.Name] = classDefinition;
        }

        /// <summary>
        /// Get FunctionDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the function</param>
        /// <param name="argumentsSize">count of function arguments, useful in case there are multiple functions with the same name but with different length of arguments declared</param>
        /// <returns></returns>
        public FunctionDefinition? GetFunction(string name, int argumentsSize)
        {
            if (_functionIndex.TryGetValue((name, argumentsSize), out var functionDefinition))
                return functionDefinition;

            return _parent?.GetFunction(name, argumentsSize);
        }

        /// <summary>
        /// Check that DefinitionScope contains the function
        /// </summary>
        /// <param name="name">name of the function</param>
        /// <param name="argumentsSize">amount of function arguments in case there are multiple functions with the same name declared</param>
        /// <returns></returns>
        public bool ContainsFunction(string name, int argumentsSize)
        {
            return GetFunction(name, argumentsSize) != null;
        }

        /// <summary>
        /// Add FunctionDefinition to the current block
        /// </summary>
        public void AddFunction(FunctionDefinition functionDefinition)
        {
            // Check if a native/parent function with the same signature already exists
            var existing = _parent?.GetFunction(
                functionDefinition.Details.Name,
                functionDefinition.Details.Arguments.Count);
    
            if (existing is NativeFunctionDefinition)
                _script.Logger?.Invoke(_script, $"Shadowing native function '{functionDefinition.Details.Name}'");
            
            Functions.Add(functionDefinition);
            _functionIndex[(functionDefinition.Details.Name, functionDefinition.Details.Arguments.Count)] = functionDefinition;

            // Register at reduced arities for default parameters.
            // A function `fun f [a, b = 1, c = 2]` with MinArity=1
            // is also reachable as f[a] and f[a, b].
            var details = functionDefinition.Details;
            for (int arity = details.MinArity; arity < details.Arguments.Count; arity++)
            {
                var key = (details.Name, arity);
                if (!_functionIndex.ContainsKey(key))
                    _functionIndex[key] = functionDefinition;
            }
        }

        /// <summary>
        /// Copy all locally defined functions and classes into the target scope.
        /// Used by import to merge an imported file's definitions into the caller's scope.
        /// </summary>
        public void CopyLocalDefinitionsTo(DefinitionScope target)
        {
            foreach (var classDefinition in _classes.Values)
                target.AddClass(classDefinition);

            foreach (var functionDefinition in Functions)
                target.AddFunction(functionDefinition);
        }

        /// <summary>
        /// Remove all locally defined functions and classes.
        /// Used by hot reload to clear old definitions before re-parsing.
        /// Native functions (in the parent scope) are unaffected.
        /// </summary>
        public void Clear()
        {
            _classes.Clear();
            Functions.Clear();
            _functionIndex.Clear();
        }
    }
}