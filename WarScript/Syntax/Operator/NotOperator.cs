using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class NotOperator : UnaryOperatorExpression
    {
        public NotOperator(IExpression expression) : base(expression)
        {
        }
        
        public override IValue Calc(IValue value)
        {
            if (value is LogicalValue logicalValue)
                return new LogicalValue(!logicalValue.ValueField);
            else
                throw new Exception($"Unable to perform NOT operator for non logical value: {value}");
        }
    }
}