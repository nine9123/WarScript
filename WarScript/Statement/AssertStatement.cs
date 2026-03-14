using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class AssertStatement : Statement
    {
        public readonly IExpression Expression;
        
        public AssertStatement(int rowNumber, string blockName, IExpression expression) : base(rowNumber, blockName)
        {
            Expression = expression;
        }

        public override void Execute()
        {
            var value = Expression.Evaluate();
            if (value is LogicalValue logicalValue && !logicalValue.GetValue())
            {
                ExceptionContext.RaiseException("Assertion error");
                ExceptionContext.AddTracedStatement(this);
            }
        }
    }
}