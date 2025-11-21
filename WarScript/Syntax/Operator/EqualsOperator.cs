using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class EqualsOperator : BinaryOperatorExpression
    {
        public EqualsOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            return new LogicalValue(left.Equals(right));
        }
    }
}