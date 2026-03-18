using System;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ExponentiationOperator : BinaryOperatorExpression
    {
        public ExponentiationOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(_script, Math.Pow(leftNum.GetValue(), rightNum.GetValue()));

            return _script.ExceptionContext.RaiseException($"Unable to make exponentiation with non numeric values `{left}` and `{right}`");
        }
    }
}