using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class LogicalAndOperator : BinaryOperatorExpression
    {
        public LogicalAndOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is LogicalValue leftLog && right is LogicalValue rightLog)
                return new LogicalValue(leftLog.GetValue() && rightLog.GetValue());

            return ExceptionContext.RaiseException($"Unable to perform AND operator for non logical values `{left}`, `{right}`");
        }
    }
}