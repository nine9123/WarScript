namespace WarScript.Expression.Operator
{
    public sealed class ArrayAppendOperator : BinaryOperatorExpression
    {
        public ArrayAppendOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
