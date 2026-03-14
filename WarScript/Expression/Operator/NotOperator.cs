using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class NotOperator : UnaryOperatorExpression
    {
        public NotOperator(IExpression value) : base(value) { }

        public override IValue Evaluate()
        {
            var value = Value.Evaluate();
            if (value == null) return null;

            if (value is LogicalValue logicalValue)
                return new LogicalValue(!logicalValue.GetValue());

            return ExceptionContext.RaiseException($"Unable to perform NOT operator for non logical value `{value}`");
        }
    }
}