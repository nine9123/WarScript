using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ArrayValueOperator : BinaryOperatorExpression, IAssignExpression
    {
        public ArrayValueOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is ArrayValue leftArr && right is NumericValue numericValue)
                return leftArr.GetValue((int)numericValue.GetValue());

            if (left is TextValue leftText && right is NumericValue numericValue2)
                return leftText.GetValue((int)numericValue2.GetValue());

            return left;
        }

        public IValue Assign(IValue value)
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is ArrayValue leftArr && right is NumericValue numericValue)
                leftArr.SetValue((int)numericValue.GetValue(), value);

            if (left is TextValue leftText && right is NumericValue numericValue2)
                leftText.SetValue((int)numericValue2.GetValue(), value);

            return left;
        }
    }
}