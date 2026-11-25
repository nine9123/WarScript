using WarScript.Expression;

namespace WarScript.Statement
{
    public class ReturnStatement : Statement
    {
        private readonly IExpression _expression;
        public IExpression Expression => _expression;

        public ReturnStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            _expression = expression;
        }
    }
}
