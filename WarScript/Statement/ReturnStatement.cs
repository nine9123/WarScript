using WarScript.Context;
using WarScript.Expression;

namespace WarScript.Statement
{
    public class ReturnStatement : Statement
    {
        private readonly IExpression _expression;

        public ReturnStatement(int rowNumber, string blockName, IExpression expression) : base(rowNumber, blockName)
        {
            _expression = expression;
        }

        public override void Execute()
        {
            var result = _expression.Evaluate();
            if (result != null)
            {
                ReturnContext.GetScope().Invoke(result);
            }
            ExceptionContext.AddTracedStatement(this);
        }
    }
}