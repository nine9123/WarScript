using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NestedClassInstanceOperator : BinaryOperatorExpression
    {
        public NestedClassInstanceOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsClass && Right is ClassExpression classExpr)
                return classExpr.Evaluate(left.ClassValue);

            return _script.RaiseException($"Unable to access class's nested class `{Right}`");
        }
    }
}
