using WarScript.Context;
using WarScript.Expression;

namespace WarScript.Statement
{
    public class ExpressionStatement : Statement
    {
        public readonly IExpression Expression;

        public ExpressionStatement(int rowNumber, string blockName, IExpression expression) : base(rowNumber, blockName)
        {
            Expression = expression;
        }
        
        public override void Execute()
        {
            Expression.Evaluate();
            ExceptionContext.AddTracedStatement(this);
        }
    }
}