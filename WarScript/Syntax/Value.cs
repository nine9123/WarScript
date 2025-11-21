using System;

namespace WarScript.Syntax
{
    public class Value<T> : IValue where T : IComparable<T>
    {
        public readonly T ValueField;

        public Value(T value)
        {
            ValueField = value;
        }

        public override string ToString()
        {
            return ValueField.ToString();
        }

        public IValue Evaluate()
        {
            return this;
        }
    }
}