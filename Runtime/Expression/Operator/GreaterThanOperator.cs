namespace WarScript.Expression.Operator
{
    public sealed class GreaterThanOperator : BinaryOperatorExpression
    {
        public GreaterThanOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
