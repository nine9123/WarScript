namespace WarScript.Expression.Operator
{
    public sealed class LessThanOperator : BinaryOperatorExpression
    {
        public LessThanOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
