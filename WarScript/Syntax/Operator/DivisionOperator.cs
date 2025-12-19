using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class DivisionOperator : BinaryOperatorExpression
    {
        public DivisionOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (left is NumericValue valueOne && right is NumericValue valueTwo)
                return new NumericValue(valueOne.ValueField / valueTwo.ValueField);
            else
                throw new Exception($"Unable to perform division operator for non numerical value: {left} and {right}");
        }
    }
}