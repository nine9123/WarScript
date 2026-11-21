namespace WarScript.Expression.Operator
{
    public sealed class LogicalOrOperator : BinaryOperatorExpression
    {
        public LogicalOrOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
