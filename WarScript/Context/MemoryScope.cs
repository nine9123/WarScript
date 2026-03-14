using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Contains variables defined in a block of code
    ///
    /// <see cref="ValueReference"/>
    /// <see cref="MemoryContext"/>
    /// <see cref="Expression.Value"/>
    /// </summary>
    public class MemoryScope
    {
        /// <summary>
        /// Variables defined in this block
        /// </summary>
        private readonly Dictionary<string, ValueReference> _variables;
        
        /// <summary>
        /// Parent MemoryScope to access the variables defined in outer scopes
        /// </summary>
        private readonly MemoryScope _parent;

        public MemoryScope(MemoryScope parent)
        {
            _variables = new Dictionary<string, ValueReference>();
            _parent = parent;
        }

        /// <summary>
        /// Get variable value from the current scope or in the outer scopes
        /// </summary>
        /// <returns><see cref="NullValue"/> if there is no variable defined</returns>
        public IValue Get(string name)
        {
            _variables.TryGetValue(name, out var variable);
            if (variable != null)
                return variable.Value;
            else if (_parent != null)
                return _parent.Get(name);
            else
                return NullValue.Instance;
        }

        /// <summary>
        /// Get variable from the current scope
        /// </summary>
        public IValue GetLocal(string name)
        {
            _variables.TryGetValue(name, out var variable);
            return variable != null ? variable.Value : null;
        }

        /// <summary>
        /// Set variable's value to the current scope
        /// </summary>
        public void Set(string name, IValue value)
        {
            var variableScope = FindScope(name);
            if (variableScope == null)
            {
                SetLocal(name, value);
            }
            else
            {
                variableScope.SetLocal(name, value);
            }
        }

        /// <summary>
        /// Set variable's value directly using <see cref="ValueReference"/> in the current scope
        /// </summary>
        public void SetLocal(string name, ValueReference variable)
        {
            _variables[name] = variable;
        }

        /// <summary>
        /// Set variable's value in the current scope
        /// </summary>
        public void SetLocal(string name, IValue value)
        {
            if (_variables.ContainsKey(name))
                _variables[name].Value = value;
            else
                _variables.Add(name, ValueReference.InstanceOf(value));
        }

        private MemoryScope FindScope(string name)
        {
            if (_variables.ContainsKey(name))
                return this;
            return _parent == null ? null : _parent.FindScope(name);
        }
    }
}