namespace WarScript.Expression.Operator
{
    public sealed class EqualsOperator : BinaryOperatorExpression
    {
        public EqualsOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
