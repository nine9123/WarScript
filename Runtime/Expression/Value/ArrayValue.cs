#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace WarScript.Expression.Value
{
    public class ArrayValue : IterableValue<List<IValue?>>
    {
        public ArrayValue(WarScriptLanguage script, ArrayExpression expression) : base(script, expression.GetValues())
        {
        }

        public ArrayValue(WarScriptLanguage script, List<IValue?> values) : base(script, values)
        {
        }

        public IValue? GetValue(int index)
        {
            if (GetValue().Count > index)
                return GetValue()[index];
            return _script.Null;
        }

        public void SetValue(int index, IValue? value)
        {
            if (GetValue().Count > index)
                GetValue()[index] = value;
        }

        public void AppendValue(IValue value)
        {
            GetValue().Add(value);
        }
        
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;

            var other = (ArrayValue)obj;
            return GetValue().SequenceEqual(other.GetValue());
        }
        
        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var val in GetValue())
            {
                hash = hash * 31 + (val?.GetHashCode() ?? 0);
            }
            return hash;
        }
        
        public override IEnumerator<IValue> GetEnumerator()
        {
            return GetValue().GetEnumerator();
        }
        
        public override string ToString()
        {
            return "[" + string.Join(", ", GetValue().Select(v => v?.ToString() ?? "null")) + "]";
        }
    }
}