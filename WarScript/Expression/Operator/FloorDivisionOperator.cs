using System;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class FloorDivisionOperator : BinaryOperatorExpression
    {
        public FloorDivisionOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == NullValue.Instance || right == NullValue.Instance)
                return ExceptionContext.RaiseException($"Unable to perform floor division for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(Math.Floor(leftNum.GetValue() / rightNum.GetValue()));

            return ExceptionContext.RaiseException($"Unable to divide non numeric values `{left}` and `{right}`");
        }
    }
}