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
        private MemoryScope _parent;

        private readonly WarScriptLanguage _script;

        /// <summary>
        /// Whether this scope can be returned to the object pool on EndScope.
        /// False for class instance scopes that outlive the scope stack.
        /// </summary>
        internal bool Poolable;

        public MemoryScope(WarScriptLanguage script, MemoryScope parent, bool poolable = true)
        {
            _script = script;
            _variables = new Dictionary<string, ValueReference>();
            _parent = parent;
            Poolable = poolable;
        }

        /// <summary>
        /// Reset this scope for reuse from the pool.
        /// Clears all variables and sets a new parent.
        /// </summary>
        internal void Reset(MemoryScope parent)
        {
            _variables.Clear();
            _parent = parent;
            Poolable = true;
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
                return _script.Null;
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
        /// Walks up the scope chain with a single TryGetValue per level.
        /// If the variable exists anywhere in the chain, updates it in-place.
        /// Otherwise creates a new local in the current scope.
        /// </summary>
        public void Set(string name, IValue value)
        {
            // Walk up the scope chain looking for an existing variable.
            // Uses TryGetValue which does a single hash lookup per scope,
            // instead of the old FindScope (ContainsKey) + SetLocal (ContainsKey + indexer)
            // pattern which did 2-3 lookups per scope visited.
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

            // Not found anywhere — create new local in current scope
            _variables.Add(name, ValueReference.InstanceOf(value));
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
            if (_variables.TryGetValue(name, out var existing))
                existing.Value = value;
            else
                _variables.Add(name, ValueReference.InstanceOf(value));
        }

        
        /// <summary>
        /// Returns all local variables in this scope (not parent scopes).
        /// Used by coroutines to save state at yield points.
        /// </summary>
        public Dictionary<string, IValue> GetAllLocals()
        {
            var result = new Dictionary<string, IValue>();
            foreach (var kvp in _variables)
            {
                result[kvp.Key] = kvp.Value.Value;
            }
            return result;
        }
    }
}