using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ArrayAppendOperator : BinaryOperatorExpression
    {
        public ArrayAppendOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is ArrayValue arrayValue)
            {
                arrayValue.AppendValue(right);
            }

            return left;
        }
    }
}