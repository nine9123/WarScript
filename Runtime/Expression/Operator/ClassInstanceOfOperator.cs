namespace WarScript.Expression.Operator
{
    public sealed class ClassInstanceOfOperator : BinaryOperatorExpression
    {
        public ClassInstanceOfOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
