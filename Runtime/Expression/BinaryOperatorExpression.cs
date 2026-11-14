namespace WarScript.Expression
{
    public abstract class BinaryOperatorExpression : IOperatorExpression, IPositionedExpression
    {
        public IExpression Left { get; private set; }
        public IExpression Right { get; private set; }
        protected WarScriptLanguage _script;

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Line { get; internal set; }

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Column { get; internal set; }

        protected BinaryOperatorExpression(WarScriptLanguage script, IExpression left, IExpression right)
        {
            _script = script;
            Left = left;
            Right = right;
        }
    }
}
