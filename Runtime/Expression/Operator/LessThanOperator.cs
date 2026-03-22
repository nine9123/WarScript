using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class LessThanOperator : BinaryOperatorExpression
    {
        public LessThanOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNull || right.IsNull)
                return _script.RaiseException($"Unable to perform less than for NULL values `{left}`, `{right}`");

            return WarValue.FromLogical(left.CompareTo(right) < 0);
        }
    }
}
