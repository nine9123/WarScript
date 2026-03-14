#nullable enable

using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public interface IAssignExpression
    {
        IValue? Assign(IValue? value);
    }
}