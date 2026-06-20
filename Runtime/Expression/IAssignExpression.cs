using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public interface IAssignExpression
    {
        WarValue Assign(WarValue value);
    }
}
