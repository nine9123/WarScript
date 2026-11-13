namespace WarScript.Expression.Operator
{
    public sealed class NegateOperator : UnaryOperatorExpression
    {
        public NegateOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }
    }
}
