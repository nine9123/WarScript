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
        public ClassStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }
    }
}