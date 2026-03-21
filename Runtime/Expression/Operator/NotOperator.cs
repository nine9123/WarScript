using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NotOperator : UnaryOperatorExpression
    {
        public NotOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override IValue Evaluate()
        {
            var value = Value.Evaluate();
            if (value == null) return null;

            if (value is LogicalValue logicalValue)
                return logicalValue.GetValue() ? _script.LogicalFalse : _script.LogicalTrue;

            return _script.ExceptionContext.RaiseException($"Unable to perform NOT operator for non logical value `{value}`");
        }
    }
}