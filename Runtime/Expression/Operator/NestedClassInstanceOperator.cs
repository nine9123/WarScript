namespace WarScript.Expression.Operator
{
    public sealed class NestedClassInstanceOperator : BinaryOperatorExpression
    {
        public NestedClassInstanceOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
