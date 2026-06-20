using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class MultiplicationOperator : BinaryOperatorExpression
    {
        public MultiplicationOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNull || right.IsNull)
                return _script.RaiseException($"Unable to perform multiplication for NULL values `{left}`, `{right}`");

            if (left.IsNumeric && right.IsNumeric)
                return WarValue.FromNumeric(left.Numeric * right.Numeric);

            if (left.IsNumeric)
                return WarValue.FromText(WarValue.RepeatString(right.ToString(), WarValue.ToInt(left.Numeric)));
            if (right.IsNumeric)
                return WarValue.FromText(WarValue.RepeatString(left.ToString(), WarValue.ToInt(right.Numeric)));

            return _script.RaiseException($"Unable to multiply non numeric values `{left}` and `{right}`");
        }
    }
}
