using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class AdditionOperator : BinaryOperatorExpression
    {
        public AdditionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNumeric && right.IsNumeric)
                return WarValue.FromNumeric(left.Numeric + right.Numeric);

            if (left.IsArray || right.IsArray)
            {
                List<WarValue> newArray;
                if (left.IsArray && right.IsArray)
                {
                    var lv = left.ArrayValue;
                    var rv = right.ArrayValue;
                    newArray = new List<WarValue>(lv.Count + rv.Count);
                    newArray.AddRange(lv);
                    newArray.AddRange(rv);
                }
                else if (left.IsArray)
                {
                    var lv = left.ArrayValue;
                    newArray = new List<WarValue>(lv.Count + 1);
                    newArray.AddRange(lv);
                    newArray.Add(right);
                }
                else
                {
                    var rv = right.ArrayValue;
                    newArray = new List<WarValue>(rv.Count + 1);
                    newArray.Add(left);
                    newArray.AddRange(rv);
                }
                return WarValue.FromArray(newArray);
            }

            return WarValue.FromText(left.ToString() + right.ToString());
        }
    }
}
