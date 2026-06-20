using System.Collections.Generic;
using WarScript.Bytecode;
using WarScript.Statement;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Definition for a function
    ///
    /// <see cref="StatementParser"/>
    /// </summary>
    public class FunctionDefinition
    {
        /// <summary>
        /// Details for a function
        /// </summary>
        public FunctionDetails Details { get; private set; }
        
        /// <summary>
        /// Statement(s) defined in the function body.
        /// Nulled after bytecode compilation to free AST memory.
        /// </summary>
        public FunctionStatement Statement { get; internal set; }
        
        /// <summary>
        /// Contains nested classes and functions defined in this function
        /// </summary>
        public DefinitionScope DefinitionScope { get; private set; }

        /// <summary>
        /// Bytecode-compiled form of this function (null until compiled).
        /// </summary>
        public CompiledFunction Compiled { get; set; }

        public FunctionDefinition(FunctionDetails details, FunctionStatement statement, DefinitionScope definitionScope)
        {
            Details = details;
            Statement = statement;
            DefinitionScope = definitionScope;
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
            var other = (FunctionDefinition)obj;
            return Details != null ? Details.Equals(other.Details) : other.Details == null;
        }
 
        public override int GetHashCode()
        {
            return Details != null ? Details.GetHashCode() : 0;
        }
    }
}