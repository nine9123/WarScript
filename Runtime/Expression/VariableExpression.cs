namespace WarScript.Expression
{
    public sealed class VariableExpression : IExpression
    {
        public readonly string Name;

        /// <summary>
        /// The line and column of the name's token (T3.1), 0 when the
        /// parser made the expression up (a desugared default, const or
        /// enum). Not an <see cref="IPositionedExpression"/>: an error about
        /// a variable is reported at its statement or operator. Read by the
        /// migration tool (T3.2).
        /// </summary>
        public int Line { get; internal set; }

        /// <inheritdoc cref="Line"/>
        public int Column { get; internal set; }

        public VariableExpression(WarScriptLanguage script, string name)
        {
            Name = name;
        }
    }
}
