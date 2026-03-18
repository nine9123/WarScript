using System;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class FloorDivisionOperator : BinaryOperatorExpression
    {
        public FloorDivisionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == _script.Null || right == _script.Null)
                return _script.ExceptionContext.RaiseException($"Unable to perform floor division for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(_script, Math.Floor(leftNum.GetValue() / rightNum.GetValue()));

            return _script.ExceptionContext.RaiseException($"Unable to divide non numeric values `{left}` and `{right}`");
        }
    }
}