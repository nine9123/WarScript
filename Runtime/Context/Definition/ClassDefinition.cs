using System.Collections.Generic;
using WarScript.Statement;

namespace WarScript.Context.Definition
{
    /// <summary>
    /// Definition for a class
    ///
    /// <see cref="StatementParser"/>
    /// </summary>
    public class ClassDefinition : IDefinition
    {
        /// <summary>
        /// Details for a class
        /// </summary>
        public ClassDetails ClassDetails { get; }
        
        /// <summary>
        /// Details of the inherited (super) classes
        /// </summary>
        public List<ClassDetails> BaseTypes { get; }
        
        /// <summary>
        /// Constructor statement
        /// </summary>
        public ClassStatement Statement { get; }

        /// <summary>
        /// Contains nested classes and functions defined in this class
        /// </summary>
        private readonly DefinitionScope _definitionScope;

        public ClassDefinition(
            ClassDetails classDetails,
            List<ClassDetails> baseTypes,
            ClassStatement statement,
            DefinitionScope definitionScope)
        {
            ClassDetails = classDetails;
            BaseTypes = baseTypes;
            Statement = statement;
            _definitionScope = definitionScope;
        }

        public DefinitionScope GetDefinitionScope()
        {
            return _definitionScope;
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
            var other = (ClassDefinition)obj;
            return ClassDetails != null ? ClassDetails.Equals(other.ClassDetails) : other.ClassDetails == null;
        }
 
        public override int GetHashCode()
        {
            return ClassDetails != null ? ClassDetails.GetHashCode() : 0;
        }
    }
}
