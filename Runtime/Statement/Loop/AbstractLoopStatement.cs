namespace WarScript.Statement.Loop
{
    public abstract class AbstractLoopStatement : CompositeStatement
    {
        protected AbstractLoopStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }
    }
}