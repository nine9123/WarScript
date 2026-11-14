namespace WarScript.Expression.Operator
{
    public sealed class AssignmentOperator : BinaryOperatorExpression
    {
        public AssignmentOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
