using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class MultiplicationOperator : BinaryOperatorExpression
    {
        public MultiplicationOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (left is NumericValue valueOne && right is NumericValue valueTwo)
                return new NumericValue(valueOne.ValueField * valueTwo.ValueField);
            else
                throw new Exception($"Unable to perform multiplication operator for non numerical value: {left} and {right}");
        }
    }
}