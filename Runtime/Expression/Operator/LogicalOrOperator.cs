using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class LogicalOrOperator : BinaryOperatorExpression
    {
        public LogicalOrOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsLogical)
            {
                if (left.LogicalValue)
                    return WarValue.True;

                var right = Right.Evaluate();
                if (_script.HaltFlags != 0) return default;

                if (right.IsLogical)
                    return WarValue.FromLogical(right.LogicalValue);

                return _script.RaiseException($"Unable to perform OR operator for non logical values `{left}`, `{right}`");
            }

            return _script.RaiseException($"Unable to perform OR operator for non logical value `{left}`");
        }
    }
}
