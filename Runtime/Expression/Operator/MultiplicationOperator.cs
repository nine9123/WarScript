using System;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class MultiplicationOperator : BinaryOperatorExpression
    {
        public MultiplicationOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == _script.Null || right == _script.Null)
                return _script.ExceptionContext.RaiseException($"Unable to perform multiplication for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(_script, leftNum.GetValue() * rightNum.GetValue());

            if (left is NumericValue leftNumOnly)
                return new TextValue(_script, right.ToString().Repeat((int)leftNumOnly.GetValue()));

            if (right is NumericValue rightNumOnly)
                return new TextValue(_script, left.ToString().Repeat((int)rightNumOnly.GetValue()));

            return _script.ExceptionContext.RaiseException($"Unable to multiply non numeric values `{left}` and `{right}`");
        }
    }
    
    public static class StringExtensions
    {
        public static string Repeat(this string s, int count)
        {
            if (count <= 0 || s.Length == 0) return string.Empty;
            if (count == 1) return s;

            return string.Create(s.Length * count, s, (span, src) =>
            {
                for (var i = 0; i < span.Length; i += src.Length)
                    src.AsSpan().CopyTo(span.Slice(i));
            });
        }
    }
}