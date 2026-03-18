using System.Collections;
using System.Collections.Generic;

namespace WarScript.Expression.Value
{
    public abstract class IterableValue<T> : Value<T>, IEnumerable<IValue>
    {
        protected IterableValue(WarScriptLanguage script, T value) : base(script, value)
        {
        }

        public abstract IEnumerator<IValue> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}