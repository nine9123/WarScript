namespace WarScript.Expression.Operator
{
    public sealed class ClassInstanceOperator : UnaryOperatorExpression
    {
        public ClassInstanceOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }
    }
}
