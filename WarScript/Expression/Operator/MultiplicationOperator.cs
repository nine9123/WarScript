using System;
using System.Linq;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class MultiplicationOperator : BinaryOperatorExpression
    {
        public MultiplicationOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == NullValue.Instance || right == NullValue.Instance)
                return ExceptionContext.RaiseException($"Unable to perform multiplication for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(leftNum.GetValue() * rightNum.GetValue());

            if (left is NumericValue leftNumOnly)
                return new TextValue(right.ToString().Repeat((int)leftNumOnly.GetValue()));

            if (right is NumericValue rightNumOnly)
                return new TextValue(left.ToString().Repeat((int)rightNumOnly.GetValue()));

            return ExceptionContext.RaiseException($"Unable to multiply non numeric values `{left}` and `{right}`");
        }
    }
    
    public static class StringExtensions
    {
        public static string Repeat(this string s, int count) 
            => new string[count].Aggregate("", (acc, _) => acc + s);
        // or more efficiently:
        // => string.Concat(Enumerable.Repeat(s, count));
    }
}