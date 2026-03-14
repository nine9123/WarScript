using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class EqualsOperator : BinaryOperatorExpression
    {
        public EqualsOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            bool result;
            if (left == NullValue.Instance || right == NullValue.Instance)
                // null equality is reference-based
                result = left == right;
            else if (left.GetType() == right.GetType())
                // same type: compare inner values
                result = left.Equals(right);
            else
                // different types: fall back to string comparison
                result = left.ToString() == right.ToString();

            return new LogicalValue(result);
        }
    }
}