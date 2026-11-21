namespace WarScript.Expression.Operator
{
    public sealed class LessThanOrEqualToOperator : BinaryOperatorExpression
    {
        public LessThanOrEqualToOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
