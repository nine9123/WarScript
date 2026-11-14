namespace WarScript.Expression.Operator
{
    public sealed class ClassCastOperator : BinaryOperatorExpression
    {
        public ClassCastOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
