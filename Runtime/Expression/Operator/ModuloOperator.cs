namespace WarScript.Expression.Operator
{
    public sealed class ModuloOperator : BinaryOperatorExpression
    {
        public ModuloOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }
    }
}
