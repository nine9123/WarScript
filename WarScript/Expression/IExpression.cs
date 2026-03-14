#nullable enable

using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public interface IExpression
    {
        IValue? Evaluate();
    }
}