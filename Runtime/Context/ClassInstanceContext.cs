using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    public class ClassInstanceContext
    {
        private readonly Stack<ClassData> _values = new Stack<ClassData>();

        /// <summary>True while executing inside a class instance (constructor or method).</summary>
        public bool HasValue => _values.Count > 0;

        public ClassData GetValue()
        {
            return _values.Peek();
        }

        public void PushValue(ClassData instance)
        {
            _values.Push(instance);
        }

        public void PopValue()
        {
            _values.Pop();
        }
    }
}
