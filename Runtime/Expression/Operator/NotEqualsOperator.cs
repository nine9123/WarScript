namespace WarScript.Expression.Operator
{
    public sealed class NotEqualsOperator : BinaryOperatorExpression
    {
        public NotEqualsOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
