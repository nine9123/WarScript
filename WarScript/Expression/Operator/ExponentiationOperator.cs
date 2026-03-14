using System;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ExponentiationOperator : BinaryOperatorExpression
    {
        public ExponentiationOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(Math.Pow(leftNum.GetValue(), rightNum.GetValue()));

            return ExceptionContext.RaiseException($"Unable to make exponentiation with non numeric values `{left}` and `{right}`");
        }
    }
}