using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class LogicalOrOperator : BinaryOperatorExpression
    {
        public LogicalOrOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // Short-circuit: if left is true, result is true without evaluating right
            if (left is LogicalValue leftLog)
            {
                if (leftLog.GetValue())
                    return _script.LogicalTrue;

                var right = Right.Evaluate();
                if (right == null) return null;

                if (right is LogicalValue rightLog)
                    return rightLog.GetValue() ? _script.LogicalTrue : _script.LogicalFalse;

                return _script.ExceptionContext.RaiseException($"Unable to perform OR operator for non logical values `{left}`, `{right}`");
            }

            return _script.ExceptionContext.RaiseException($"Unable to perform OR operator for non logical value `{left}`");
        }
    }
}