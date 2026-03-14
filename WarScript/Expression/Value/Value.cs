using System.Collections.Generic;

namespace WarScript.Expression.Value
{
    public class Value<T> : IValue
    {
        private T _value;

        public Value(T value)
        {
            SetValue(value);
        }

        public override string ToString()
        {
            return _value.ToString();
        }

        public virtual T GetValue()
        {
            return _value;
        }

        public object GetObjectValue()
        {
            return _value;
        }

        public void SetValue(T value)
        {
            _value = value;
        }

        public virtual IValue Evaluate()
        {
            return this;
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
            var other = (Value<T>)obj;
            return EqualityComparer<T>.Default.Equals(_value, other._value);
        }
 
        public override int GetHashCode()
        {
            return _value != null ? _value.GetHashCode() : 0;
        }
    }
}