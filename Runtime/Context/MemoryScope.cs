using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// What a <see cref="MemoryScope"/> was created for. Only the audit mode
    /// (plan 8.1, T0.8) reads it, to tell e.g. a function writing a global
    /// from a top-level block doing so.
    /// </summary>
    internal enum MemoryScopeKind
    {
        /// <summary>A top-level block (<c>PushScope</c>), and anything not classified below.</summary>
        Block,
        /// <summary>The host globals (<see cref="WarScriptLanguage.GlobalMemoryScope"/>).</summary>
        Native,
        /// <summary>The script's globals (<see cref="WarScriptLanguage.UserMemoryScope"/>).</summary>
        User,
        /// <summary>A function's or lambda's locals.</summary>
        FunctionLocal,
        /// <summary>A class instance's fields; the class body runs in it.</summary>
        ClassInstance,
        /// <summary>A method's locals; its parent is the instance's scope.</summary>
        MethodLocal,
    }

    public class MemoryScope
    {
        private readonly Dictionary<string, ValueReference> _variables;
        private MemoryScope _parent;
        private readonly WarScriptLanguage _script;
        internal bool Poolable;
        internal MemoryScopeKind Kind;

        public MemoryScope(WarScriptLanguage script, MemoryScope parent, bool poolable = true)
            : this(script, parent, poolable, MemoryScopeKind.Block)
        {
        }

        internal MemoryScope(WarScriptLanguage script, MemoryScope parent, bool poolable, MemoryScopeKind kind)
        {
            _script = script;
            _variables = new Dictionary<string, ValueReference>();
            _parent = parent;
            Poolable = poolable;
            Kind = kind;
        }

        internal void Reset(MemoryScope parent, MemoryScopeKind kind)
        {
            _variables.Clear();
            _parent = parent;
            Poolable = true;
            Kind = kind;
        }

        /// <summary>The scope in this one's parent chain that holds <paramref name="name"/>, or null (audit mode).</summary>
        internal MemoryScope Resolve(string name)
        {
            for (var scope = this; scope != null; scope = scope._parent)
                if (scope._variables.ContainsKey(name))
                    return scope;
            return null;
        }

        internal MemoryScope Parent => _parent;

        /// <summary>True if this scope itself (not a parent) holds <paramref name="name"/>.</summary>
        internal bool HasLocal(string name) => _variables.ContainsKey(name);

        /// <summary>The names this scope itself holds.</summary>
        internal Dictionary<string, ValueReference>.KeyCollection LocalNames => _variables.Keys;

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

        public void Set(string name, in WarValue value)
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

        public void SetLocal(string name, in WarValue value)
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
