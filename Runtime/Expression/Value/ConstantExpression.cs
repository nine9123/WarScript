namespace WarScript.Expression.Value
{
    /// <summary>
    /// Wraps a WarValue as an IExpression for use in the AST.
    /// Created during parsing for literal values (numbers, strings, booleans, null).
    /// </summary>
    public sealed class ConstantExpression : IExpression
    {
        private readonly WarValue _value;

        public ConstantExpression(WarValue value)
        {
            _value = value;
        }

        public WarValue Evaluate() => _value;
    }
}
