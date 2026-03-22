using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class SubtractionOperator : BinaryOperatorExpression
    {
        public SubtractionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNull || right.IsNull)
                return _script.RaiseException($"Unable to perform subtraction for NULL values `{left}`, `{right}`");

            if (left.IsNumeric && right.IsNumeric)
                return WarValue.FromNumeric(left.Numeric - right.Numeric);

            return WarValue.FromText(left.ToString().Replace(right.ToString(), ""));
        }
    }
}
