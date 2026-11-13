namespace WarScript.Expression
{
    public sealed class VariableExpression : IExpression
    {
        public readonly string Name;

        public VariableExpression(WarScriptLanguage script, string name)
        {
            Name = name;
        }
    }
}
