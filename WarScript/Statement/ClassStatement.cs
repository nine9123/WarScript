namespace WarScript.Statement
{
    /// <summary>
    /// Statement for constructor
    ///
    /// <see cref="Context.Definition.ClassDefinition"/>
    /// <see cref="StatementParser"/>
    /// </summary>
    public class ClassStatement : CompositeStatement
    {
        public ClassStatement(int rowNumber, string blockName) : base(rowNumber, blockName)
        {
        }
    }
}