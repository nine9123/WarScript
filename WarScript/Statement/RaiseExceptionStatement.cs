using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class RaiseExceptionStatement : Statement
    {
        private readonly IExpression _expression;

        public RaiseExceptionStatement(int rowNumber, string blockName, IExpression expression) : base(rowNumber, blockName)
        {
            _expression = expression;
        }
        
        public override void Execute()
        {
            var value = _expression.Evaluate();
            if (value != null)
            {
                if (value == NullValue.Instance)
                {
                    value = new TextValue("Empty exception");
                }
                ExceptionContext.RaiseException(value);
            }
            ExceptionContext.AddTracedStatement(this);
        }
    }
}