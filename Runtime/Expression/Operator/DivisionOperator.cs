namespace WarScript.Expression.Operator
{
    public sealed class DivisionOperator : BinaryOperatorExpression
    {
        public DivisionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
