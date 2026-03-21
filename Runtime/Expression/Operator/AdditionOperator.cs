using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class AdditionOperator : BinaryOperatorExpression
    {
        public AdditionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right)
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
                return _script.GetNumeric(leftNumericValue.GetValue() + rightNumericValue.GetValue());
            }
            else if (left is ArrayValue || right is ArrayValue)
            {
                List<IValue> newArray;
                if (left is ArrayValue leftArr && right is ArrayValue rightArr)
                {
                    var lv = leftArr.GetValue();
                    var rv = rightArr.GetValue();
                    newArray = new List<IValue>(lv.Count + rv.Count);
                    newArray.AddRange(lv);
                    newArray.AddRange(rv);
                }
                else if (left is ArrayValue leftArr2)
                {
                    var lv = leftArr2.GetValue();
                    newArray = new List<IValue>(lv.Count + 1);
                    newArray.AddRange(lv);
                    newArray.Add(right);
                }
                else
                {
                    var rv = ((ArrayValue)right).GetValue();
                    newArray = new List<IValue>(rv.Count + 1);
                    newArray.Add(left);
                    newArray.AddRange(rv);
                }

                return new ArrayValue(_script, newArray);
            }
            else
            {
                return new TextValue(_script, left.ToString() + right.ToString());
            }
        }
    }
}