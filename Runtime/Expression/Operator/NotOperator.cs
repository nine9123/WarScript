using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NotOperator : UnaryOperatorExpression
    {
        public NotOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override WarValue Evaluate()
        {
            var value = Value.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (value.IsLogical)
                return WarValue.FromLogical(!value.LogicalValue);

            return _script.RaiseException($"Unable to perform NOT operator for non logical value `{value}`");
        }
    }
}
