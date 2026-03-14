using System.Text.RegularExpressions;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class SubtractionOperator : BinaryOperatorExpression
    {
        public SubtractionOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == NullValue.Instance || right == NullValue.Instance)
                return ExceptionContext.RaiseException($"Unable to perform subtraction for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(leftNum.GetValue() - rightNum.GetValue());

            return new TextValue(Regex.Replace(left.ToString(), right.ToString(), ""));
        }
    }
}