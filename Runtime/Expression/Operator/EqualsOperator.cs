using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class EqualsOperator : BinaryOperatorExpression
    {
        public EqualsOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            bool result;
            if (left == _script.Null || right == _script.Null)
                // null equality is reference-based
                result = left == right;
            else if (left.GetType() == right.GetType())
                // same type: compare inner values
                result = left.Equals(right);
            else
                // different types: fall back to string comparison
                result = left.ToString() == right.ToString();

            return result ? _script.LogicalTrue : _script.LogicalFalse;
        }
    }
}