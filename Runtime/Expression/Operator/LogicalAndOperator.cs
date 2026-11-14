namespace WarScript.Expression.Operator
{
    public sealed class LogicalAndOperator : BinaryOperatorExpression
    {
        public LogicalAndOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
