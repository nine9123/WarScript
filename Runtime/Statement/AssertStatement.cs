using WarScript.Expression;

namespace WarScript.Statement
{
    public class AssertStatement : Statement
    {
        public readonly IExpression Expression;

        public AssertStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            Expression = expression;
        }
    }
}
