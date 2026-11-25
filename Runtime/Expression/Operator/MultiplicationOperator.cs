namespace WarScript.Expression.Operator
{
    public sealed class MultiplicationOperator : BinaryOperatorExpression
    {
        public MultiplicationOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
