namespace WarScript.Expression.Operator
{
    public sealed class ClassPropertyOperator : BinaryOperatorExpression
    {
        public ClassPropertyOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
