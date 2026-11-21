namespace WarScript.Statement
{
    public class HandleExceptionStatement : Statement
    {
        public readonly CompositeStatement BeginStatement;
        public readonly CompositeStatement RescueStatement;
        public readonly CompositeStatement EnsureStatement;
        public readonly string ErrorVariable;

        public HandleExceptionStatement(
            WarScriptLanguage script, int rowNumber, string blockName,
            CompositeStatement beginStatement,
            CompositeStatement rescueStatement,
            CompositeStatement ensureStatement,
            string errorVariable) : base(script, rowNumber, blockName)
        {
            BeginStatement = beginStatement;
            RescueStatement = rescueStatement;
            EnsureStatement = ensureStatement;
            ErrorVariable = errorVariable;
        }
    }
}
