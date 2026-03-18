namespace WarScript.Expression.Value
{
    public class LogicalValue : ComparableValue<bool>
    {
        public LogicalValue(WarScriptLanguage script, bool value) : base(script, value)
        {
        }
    }
}