using System;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class LessThanOrEqualToOperator : BinaryOperatorExpression
    {
        public LessThanOrEqualToOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == NullValue.Instance || right == NullValue.Instance)
                return ExceptionContext.RaiseException($"Unable to perform less than or equal to for NULL values `{left}`, `{right}`");

            bool result;
            if (left.GetType() == right.GetType() && left is IComparableValue)
                result = ((IComparable)left.GetObjectValue()).CompareTo(right.GetObjectValue()) <= 0;
            else
                result = string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal) <= 0;

            return new LogicalValue(result);
        }
    }
}