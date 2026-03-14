using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public abstract class BinaryOperatorExpression : IOperatorExpression
    {
        public IExpression Left { get; private set; }
        public IExpression Right { get; private set; }

        protected BinaryOperatorExpression(IExpression left, IExpression right)
        {
            Left = left;
            Right = right;
        }

        public abstract IValue Evaluate();
    }
}