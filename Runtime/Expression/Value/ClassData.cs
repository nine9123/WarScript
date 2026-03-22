#nullable enable

using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;

namespace WarScript.Expression.Value
{
    /// <summary>
    /// Holds the runtime data for a class instance.
    /// Referenced by WarValue when Tag == ValueTag.Class.
    /// </summary>
    public class ClassData
    {
        public readonly ClassDefinition Definition;
        public readonly MemoryScope MemoryScope;
        public readonly Dictionary<string, ClassData> Relations;

        public ClassData(ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassData> relations)
        {
            Definition = definition;
            MemoryScope = memoryScope;
            Relations = relations;
        }

        public ClassData? GetRelation(string name)
        {
            Relations.TryGetValue(name, out var value);
            return value;
        }

        public bool ContainsRelation(string name) =>
            Relations.ContainsKey(name);

        public WarValue GetProperty(string name)
        {
            var result = MemoryScope.GetLocal(name);
            return result;
        }

        public void SetProperty(string name, WarValue value)
        {
            MemoryScope.SetLocal(name, value);
        }

        public bool StructuralEquals(ClassData other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            var properties = Definition.ClassDetails.Properties;
            for (int i = 0; i < properties.Count; i++)
            {
                var prop = properties[i];
                var thisVal = MemoryScope.GetLocal(prop);
                var otherVal = other.MemoryScope.GetLocal(prop);
                if (!thisVal.Equals(otherVal))
                    return false;
            }
            return true;
        }

        public int StructuralHashCode()
        {
            var hash = 17;
            var properties = Definition.ClassDetails.Properties;
            for (int i = 0; i < properties.Count; i++)
            {
                var val = MemoryScope.GetLocal(properties[i]);
                hash = hash * 31 + val.GetHashCode();
            }
            return hash;
        }
    }
}
