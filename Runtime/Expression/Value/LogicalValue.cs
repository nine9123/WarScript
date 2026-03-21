namespace WarScript.Expression.Value
{
    public sealed class LogicalValue : ComparableValue<bool>
    {
        public LogicalValue(WarScriptLanguage script, bool value) : base(script, value)
        {
        }
    }
}