namespace WarScript.Expression
{
    public abstract class UnaryOperatorExpression : IOperatorExpression, IPositionedExpression
    {
        public IExpression Value { get; private set; }
        protected WarScriptLanguage _script;

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Line { get; internal set; }

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Column { get; internal set; }

        protected UnaryOperatorExpression(WarScriptLanguage script, IExpression value)
        {
            _script = script;
            Value = value;
        }
    }
}
