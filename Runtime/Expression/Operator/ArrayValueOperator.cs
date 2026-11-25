namespace WarScript.Expression.Operator
{
    public sealed class ArrayValueOperator : BinaryOperatorExpression
    {
        public ArrayValueOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
