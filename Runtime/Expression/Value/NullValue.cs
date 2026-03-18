using System;

namespace WarScript.Expression.Value
{
    public sealed class NullValue : Value<NullValue.NullSentinel>
    {
        public sealed class NullSentinel : IComparable<NullSentinel>
        {
            public int CompareTo(NullSentinel other) => 0;
        }
        
        public NullValue(WarScriptLanguage script) : base(script, null) { }

        public override string ToString()
        {
            return "null";
        }
    }
}