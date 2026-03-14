using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public abstract class UnaryOperatorExpression : IOperatorExpression
    {
        public IExpression Value { get; private set; }

        protected UnaryOperatorExpression(IExpression value)
        {
            Value = value;
        }

        public abstract IValue Evaluate();
    }
}