using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public interface IExpression
    {
        WarValue Evaluate();
    }
}
