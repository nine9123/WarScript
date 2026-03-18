#nullable enable

using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class VariableExpression : IExpression, IAssignExpression
    {
        public readonly string Name;

        private readonly WarScriptLanguage _script;
        
        public VariableExpression(WarScriptLanguage script, string name)
        {
            _script = script;
            Name = name;
        }

        public IValue Evaluate()
        {
            return _script.MemoryContext.GetScope().Get(Name);
        }

        public IValue? Assign(IValue? value)
        {
            if (value == null) return null;
            _script.MemoryContext.GetScope().Set(Name, value);
            return value;
        }
    }
}