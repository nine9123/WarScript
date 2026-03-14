using System;

namespace WarScript.Expression.Value
{
    public class ComparableValue<T> : Value<T>, IComparableValue where T : IComparable<T>
    {
        public ComparableValue(T value) : base(value)
        {
        }
    }
}