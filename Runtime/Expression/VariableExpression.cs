using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public sealed class VariableExpression : IExpression, IAssignExpression
    {
        public readonly string Name;
        private readonly WarScriptLanguage _script;

        public VariableExpression(WarScriptLanguage script, string name)
        {
            _script = script;
            Name = name;
        }

        public WarValue Evaluate()
        {
            return _script.MemoryContext.GetScope().Get(Name);
        }

        public WarValue Assign(WarValue value)
        {
            _script.MemoryContext.GetScope().Set(Name, value);
            return value;
        }
    }
}
