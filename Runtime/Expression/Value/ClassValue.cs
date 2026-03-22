#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;

namespace WarScript.Expression.Value
{
    public class ClassValue : IterableValue<ClassDefinition>
    {
        public MemoryScope MemoryScope { get; private set; }
        
        // contains ClassValue for the Derived class and all the Base classes chain that Derived class inherits
        public Dictionary<string, ClassValue> Relations { get; private set; }

        public ClassValue(WarScriptLanguage script, ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassValue> relations) : base(script, definition)
        {
            MemoryScope = memoryScope;
            Relations = relations;
        }

        public ClassValue? GetRelation(string name)
        {
            if (Relations.TryGetValue(name, out var value))
                return value;

            return null;
        }

        public bool ContainsRelation(string name)
        {
            return Relations.ContainsKey(name);
        }
        
        public IValue GetValue(string name)
        {
            // Read directly from the class instance's scope.
            // No need to push/pop the global scope stack — the MemoryScope
            // is self-contained and GetLocal is a simple dictionary lookup.
            var result = MemoryScope.GetLocal(name);
            return result ?? _script.Null;
        }

        public void SetValue(string name, IValue? value)
        {
            // Write directly to the class instance's scope.
            MemoryScope.SetLocal(name, value);
        }
        
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
    
            var other = (ClassValue)obj;
            var properties = GetValue().ClassDetails.Properties;
    
            // Compare all properties with direct scope reads — no push/pop per property.
            for (int i = 0; i < properties.Count; i++)
            {
                var prop = properties[i];
                var thisVal = MemoryScope.GetLocal(prop);
                var otherVal = other.MemoryScope.GetLocal(prop);
                
                var thisResolved = thisVal ?? _script.Null;
                var otherResolved = otherVal ?? (IValue)_script.Null;
                
                if (!thisResolved.Equals(otherResolved))
                    return false;
            }
            return true;
        }
        
        public override int GetHashCode()
        {
            var hash = 17;
            var properties = GetValue().ClassDetails.Properties;
            for (int i = 0; i < properties.Count; i++)
            {
                var val = MemoryScope.GetLocal(properties[i]);
                hash = hash * 31 + (val?.GetHashCode() ?? 0);
            }
            return hash;
        }
        
        public override IEnumerator<IValue> GetEnumerator()
        {
            var properties = GetValue().ClassDetails.Properties;
            for (int i = 0; i < properties.Count; i++)
            {
                var val = MemoryScope.GetLocal(properties[i]);
                yield return val ?? (IValue)_script.Null;
            }
        }
    }
}