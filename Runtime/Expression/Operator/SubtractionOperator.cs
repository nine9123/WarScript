namespace WarScript.Expression.Operator
{
    public sealed class SubtractionOperator : BinaryOperatorExpression
    {
        public SubtractionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
