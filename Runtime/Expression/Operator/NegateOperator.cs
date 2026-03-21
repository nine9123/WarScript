using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NegateOperator : UnaryOperatorExpression
    {
        public NegateOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override IValue Evaluate()
        {
            var value = Value.Evaluate();
            if (value == null) return null;

            if (value is NumericValue numericValue)
                return _script.GetNumeric(-numericValue.GetValue());

            return _script.ExceptionContext.RaiseException(
                $"Unable to negate non-numeric value `{value}`");
        }
    }
}