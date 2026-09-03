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
            if (!_script.ClassInstanceContext.HasValue)
            {
                _script.ExceptionContext.RaiseException("'this' can only be used inside a class");
                return WarValue.Null;
            }
            return WarValue.FromClass(_script.ClassInstanceContext.GetValue());
        }
    }
}
