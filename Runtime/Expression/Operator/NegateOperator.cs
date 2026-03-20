using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class NegateOperator : UnaryOperatorExpression
    {
        public NegateOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override IValue Evaluate()
        {
            var value = Value.Evaluate();
            if (value == null) return null;

            if (value is NumericValue numericValue)
                return new NumericValue(_script, -numericValue.GetValue());

            return _script.ExceptionContext.RaiseException(
                $"Unable to negate non-numeric value `{value}`");
        }
    }
}