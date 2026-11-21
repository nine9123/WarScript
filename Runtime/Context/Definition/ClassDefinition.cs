using System.Collections.Generic;
using WarScript.Bytecode;
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
        /// Constructor statement.
        /// Nulled after bytecode compilation to free AST memory.
        /// </summary>
        public ClassStatement Statement { get; internal set; }

        /// <summary>
        /// Contains nested classes and functions defined in this class
        /// </summary>
        private readonly DefinitionScope _definitionScope;

        /// <summary>
        /// Bytecode-compiled constructor body (null until compiled).
        /// </summary>
        public CompiledFunction CompiledConstructor { get; set; }

        /// <summary>
        /// The fields this class declares itself (5.7): its constructor
        /// parameters and the names its class body assigns (bare, or as
        /// <c>@x</c> / <c>this :: x</c>) without declaring them <c>global</c>.
        /// An instance's fields are those of every class part it has (5.5).
        /// Set by the compiler, and by the loader from the file; null for a
        /// class the host registered, whose fields are not known, so that
        /// accessing any property of its instances is allowed (PROP-15, T3.6).
        /// </summary>
        internal HashSet<string>? Fields { get; set; }

        /// <summary>
        /// True for the class of an <c>enum</c> (the parser's desugar, a
        /// host <c>[WsEnum]</c>, a native manifest's <c>enum</c>; stored in
        /// the file since format 12). Its members are constant (T3.8,
        /// PROP-28): a script's property write to an instance of it is a
        /// TypeError (<c>WarVM</c>'s <c>SetProperty</c>), so the compiler
        /// may type <c>E :: X</c> from the declaration.
        /// </summary>
        internal bool IsEnum { get; set; }

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
