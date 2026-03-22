using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    public class MemoryScope
    {
        private readonly Dictionary<string, ValueReference> _variables;
        private MemoryScope _parent;
        private readonly WarScriptLanguage _script;
        internal bool Poolable;

        public MemoryScope(WarScriptLanguage script, MemoryScope parent, bool poolable = true)
        {
            _script = script;
            _variables = new Dictionary<string, ValueReference>();
            _parent = parent;
            Poolable = poolable;
        }

        internal void Reset(MemoryScope parent)
        {
            _variables.Clear();
            _parent = parent;
            Poolable = true;
        }

        public WarValue Get(string name)
        {
            if (_variables.TryGetValue(name, out var variable))
                return variable.Value;
            if (_parent != null)
                return _parent.Get(name);
            return WarValue.Null;
        }

        public WarValue GetLocal(string name)
        {
            if (_variables.TryGetValue(name, out var variable))
                return variable.Value;
            return WarValue.Null;
        }

        public void Set(string name, WarValue value)
        {
            var scope = this;
            while (scope != null)
            {
                if (scope._variables.TryGetValue(name, out var existing))
                {
                    existing.Value = value;
                    return;
                }
                scope = scope._parent;
            }
            _variables.Add(name, ValueReference.InstanceOf(value));
        }

        public void SetLocal(string name, ValueReference variable)
        {
            _variables[name] = variable;
        }

        public void SetLocal(string name, WarValue value)
        {
            if (_variables.TryGetValue(name, out var existing))
                existing.Value = value;
            else
                _variables.Add(name, ValueReference.InstanceOf(value));
        }

        public Dictionary<string, WarValue> GetAllLocals()
        {
            var result = new Dictionary<string, WarValue>();
            foreach (var kvp in _variables)
                result[kvp.Key] = kvp.Value.Value;
            return result;
        }
    }
}
