using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ClassInstanceOfOperator : BinaryOperatorExpression
    {
        public ClassInstanceOfOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsClass && Right is VariableExpression varExpr)
                return WarValue.FromLogical(left.ClassValue.ContainsRelation(varExpr.Name));

            return _script.RaiseException($"Unable to perform `is` operator for the following operands `{left}` and `{Right}`");
        }
    }
}
