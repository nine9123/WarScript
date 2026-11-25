using WarScript.Expression;

namespace WarScript.Statement
{
    public class PrintStatement : Statement
    {
        public readonly IExpression Expression;

        public PrintStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            Expression = expression;
        }
    }
}
