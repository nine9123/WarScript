namespace WarScript.Expression.Operator
{
    public sealed class NotOperator : UnaryOperatorExpression
    {
        public NotOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }
    }
}
