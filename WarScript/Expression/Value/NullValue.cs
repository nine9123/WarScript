using System;

namespace WarScript.Expression.Value
{
    public sealed class NullValue : Value<NullValue.NullSentinel>
    {
        public sealed class NullSentinel : IComparable<NullSentinel>
        {
            public int CompareTo(NullSentinel other) => 0;
        }
        
        public static readonly NullValue Instance = new NullValue();
        
        private NullValue() : base(null)
        {
        }

        public override string ToString()
        {
            return "null";
        }
    }
}