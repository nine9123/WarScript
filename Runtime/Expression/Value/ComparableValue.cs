using System;

namespace WarScript.Expression.Value
{
    public class ComparableValue<T> : Value<T>, IComparableValue where T : IComparable<T>
    {
        protected ComparableValue(WarScriptLanguage script, T value) : base(script, value)
        {
        }
    }
}