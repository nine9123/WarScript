using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class SubtractionOperator : BinaryOperatorExpression
    {
        public SubtractionOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (left is NumericValue valueOne && right is NumericValue valueTwo)
                return new NumericValue(valueOne.ValueField - valueTwo.ValueField);
            else
                return new TextValue(left.ToString().Replace(right.ToString(), ""));
        }
    }
}