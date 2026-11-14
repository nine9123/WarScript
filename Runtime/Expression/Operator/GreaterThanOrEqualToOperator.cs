namespace WarScript.Expression.Operator
{
    public sealed class GreaterThanOrEqualToOperator : BinaryOperatorExpression
    {
        public GreaterThanOrEqualToOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
