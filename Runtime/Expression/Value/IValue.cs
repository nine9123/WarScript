namespace WarScript.Expression.Value
{
    public interface IValue : IExpression
    {
        object GetObjectValue();
    }
}