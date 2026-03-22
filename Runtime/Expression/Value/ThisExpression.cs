namespace WarScript.Expression.Value
{
    /// <summary>
    /// Evaluates to the current class instance (the 'this' reference).
    /// </summary>
    public sealed class ThisExpression : IExpression
    {
        private readonly WarScriptLanguage _script;

        public ThisExpression(WarScriptLanguage script)
        {
            _script = script;
        }

        public WarValue Evaluate()
        {
            return WarValue.FromClass(_script.ClassInstanceContext.GetValue());
        }
    }
}
