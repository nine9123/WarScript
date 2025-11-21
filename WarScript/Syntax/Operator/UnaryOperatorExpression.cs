using System;

namespace WarScript.Syntax.Operator
{
    public abstract class UnaryOperatorExpression : IOperatorExpression
    {
        public IExpression Value { get; private set; }

        protected UnaryOperatorExpression(IExpression value)
        {
            Value = value;
        }

        public abstract IValue Calc(IValue value);

        public IValue Evaluate()
        {
            return Calc(Value.Evaluate());
        }
    }
}