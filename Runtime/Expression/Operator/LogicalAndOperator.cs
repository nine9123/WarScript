using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class LogicalAndOperator : BinaryOperatorExpression
    {
        public LogicalAndOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // Short-circuit: if left is false, result is false without evaluating right
            if (left is LogicalValue leftLog)
            {
                if (!leftLog.GetValue())
                    return new LogicalValue(_script, false);

                var right = Right.Evaluate();
                if (right == null) return null;

                if (right is LogicalValue rightLog)
                    return new LogicalValue(_script, rightLog.GetValue());

                return _script.ExceptionContext.RaiseException($"Unable to perform AND operator for non logical values `{left}`, `{right}`");
            }

            return _script.ExceptionContext.RaiseException($"Unable to perform AND operator for non logical value `{left}`");
        }
    }
}