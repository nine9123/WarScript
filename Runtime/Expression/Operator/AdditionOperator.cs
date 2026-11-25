namespace WarScript.Expression.Operator
{
    public sealed class AdditionOperator : BinaryOperatorExpression
    {
        public AdditionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
