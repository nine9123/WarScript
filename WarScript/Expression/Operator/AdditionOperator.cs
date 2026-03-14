using System.Collections.Generic;
using System.Linq;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class AdditionOperator : BinaryOperatorExpression
    {
        public AdditionOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is NumericValue leftNumericValue && right is NumericValue rightNumericValue)
            {
                return new NumericValue(leftNumericValue.GetValue() + rightNumericValue.GetValue());
            }
            else if (left is ArrayValue || right is ArrayValue)
            {
                List<IValue> newArray;
                if (left is ArrayValue leftArrayValue && right is ArrayValue rightArrayValue)
                {
                    newArray = leftArrayValue.GetValue().Concat(rightArrayValue.GetValue()).ToList();
                }
                else if (left is ArrayValue leftArrayValue2)
                {
                    newArray = leftArrayValue2.GetValue().Append(right).ToList();
                }
                else
                {
                    var rightArrayValue2 = (ArrayValue)right;
                    newArray = rightArrayValue2.GetValue().Prepend(left).ToList();
                }

                return new ArrayValue(newArray);
            }
            else
            {
                return new TextValue(left.ToString() + right.ToString());
            }
        }
    }
}