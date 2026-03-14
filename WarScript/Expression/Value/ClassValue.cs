#nullable enable

using System.Collections.Generic;
using System.Linq;
using WarScript.Context;
using WarScript.Context.Definition;

namespace WarScript.Expression.Value
{
    public class ClassValue : IterableValue<ClassDefinition>
    {
        public MemoryScope MemoryScope { get; private set; }
        
        // contains ClassValue for the Derived class and all the Base classes chain that Derived class inherits
        public Dictionary<string, ClassValue> Relations { get; private set; }

        public ClassValue(ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassValue> relations) : base(definition)
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
            MemoryContext.PushScope(MemoryScope);
            try
            {
                var result = MemoryContext.GetScope().GetLocal(name);
                return result == null ? NullValue.Instance : result;
            }
            finally
            {
                MemoryContext.EndScope();
            }
        }

        public void SetValue(string name, IValue? value)
        {
            MemoryContext.PushScope(MemoryScope);
            try
            {
                MemoryContext.GetScope().SetLocal(name, value);
            }
            finally
            {
                MemoryContext.EndScope();
            }
        }
        
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj == null || GetType() != obj.GetType()) return false;
    
            var other = (ClassValue)obj;
    
            return GetValue()
                .ClassDetails
                .Properties
                .All(prop => GetValue(prop).Equals(other.GetValue(prop)));
        }
        
        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var prop in GetValue().ClassDetails.Properties)
            {
                hash = hash * 31 + (GetValue(prop)?.GetHashCode() ?? 0);
            }
            return hash;
        }
        
        public override IEnumerator<IValue> GetEnumerator()
        {
            foreach (var prop in GetValue().ClassDetails.Properties)
            {
                yield return GetValue(prop);
            }
        }
    }
}