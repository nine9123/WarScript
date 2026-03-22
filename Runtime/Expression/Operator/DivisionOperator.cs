using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class DivisionOperator : BinaryOperatorExpression
    {
        public DivisionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNumeric && right.IsNumeric)
                return WarValue.FromNumeric(left.Numeric / right.Numeric);

            return _script.RaiseException($"Unable to divide non numeric values `{left}` and `{right}`");
        }
    }
}
