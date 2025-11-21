namespace WarScript.Syntax.Operator
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

        public abstract IValue Calc(IValue left, IValue right);

        public IValue Evaluate()
        {
            return Calc(Left.Evaluate(), Right.Evaluate());
        }
    }
}