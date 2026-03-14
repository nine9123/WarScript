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
        /// Classes defined in the block
        /// </summary>
        private HashSet<ClassDefinition> _classes;

        /// <summary>
        /// Functions declared in the block
        /// </summary>
        private HashSet<FunctionDefinition> _functions;
        
        /// <summary>
        /// Parent DefinitionScope to access the structures defined in outer blocks of code
        /// </summary>
        public DefinitionScope? Parent { get; private set; }

        public DefinitionScope(DefinitionScope? parent)
        {
            _classes = new HashSet<ClassDefinition>();
            _functions = new HashSet<FunctionDefinition>();
            Parent = parent;
        }

        /// <summary>
        /// Get ClassDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the class</param>
        public ClassDefinition? GetClass(string name)
        {
            foreach (var classDefinition in _classes)
            {
                if (classDefinition.ClassDetails.Name == name)
                    return classDefinition;
            }

            return Parent?.GetClass(name);
        }

        /// <summary>
        /// Add ClassDefinition to the current block
        /// </summary>
        public void AddClass(ClassDefinition classDefinition)
        {
            _classes.Add(classDefinition);
        }

        /// <summary>
        /// Get FunctionDefinition from the current block and from outer blocks of code
        /// </summary>
        /// <param name="name">name of the function</param>
        /// <param name="argumentsSize">count of function arguments, useful in case there are multiple functions with the same name but with different length of arguments declared</param>
        /// <returns></returns>
        public FunctionDefinition? GetFunction(string name, int argumentsSize)
        {
            foreach (var functionDefinition in _functions)
            {
                if (functionDefinition.Details.Name == name &&
                    functionDefinition.Details.Arguments.Count == argumentsSize)
                {
                    return functionDefinition;
                }
            }

            return Parent?.GetFunction(name, argumentsSize);
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
            _functions.Add(functionDefinition);
        }
    }
}