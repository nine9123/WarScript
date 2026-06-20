using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NegateOperator : UnaryOperatorExpression
    {
        public NegateOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override WarValue Evaluate()
        {
            var value = Value.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (value.IsNumeric)
                return WarValue.FromNumeric(-value.Numeric);

            return _script.RaiseException($"Unable to negate non-numeric value `{value}`");
        }
    }
}
