using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    public class ClassInstanceContext
    {
        private readonly Stack<ClassData> _values = new Stack<ClassData>();

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
