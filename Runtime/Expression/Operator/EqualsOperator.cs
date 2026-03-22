using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class EqualsOperator : BinaryOperatorExpression
    {
        public EqualsOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            bool result;
            if (left.IsNull || right.IsNull)
                result = left.IsNull && right.IsNull;
            else if (left.Tag == right.Tag)
                result = left.Equals(right);
            else
                result = left.ToString() == right.ToString();

            return WarValue.FromLogical(result);
        }
    }
}
