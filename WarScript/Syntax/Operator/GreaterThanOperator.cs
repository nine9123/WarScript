using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class GreaterThanOperator : BinaryOperatorExpression
    {
        public GreaterThanOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (left is NumericValue leftNumericValue && right is NumericValue rightNumericValue)
                return new LogicalValue(leftNumericValue.ValueField > rightNumericValue.ValueField);
            else
                throw new Exception("Can not compare non-numeric values");
        }
    }
}