#nullable enable

using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class VariableExpression : IExpression, IAssignExpression
    {
        public readonly string Name;

        public VariableExpression(string name)
        {
            Name = name;
        }

        public IValue Evaluate()
        {
            return MemoryContext.GetScope().Get(Name);
        }

        public IValue? Assign(IValue? value)
        {
            if (value == null) return null;
            MemoryContext.GetScope().Set(Name, value);
            return value;
        }
    }
}