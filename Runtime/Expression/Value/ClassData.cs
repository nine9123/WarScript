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

        /// <summary>
        /// Indexed property storage — parallel to Definition.ClassDetails.Properties.
        /// Enables O(1) property access when the property index is known (inline cache).
        /// Shares ValueReferences with inherited classes for mutation propagation.
        /// </summary>
        internal ValueReference[] PropertyValues;

        public ClassData(ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassData> relations)
        {
            Definition = definition;
            MemoryScope = memoryScope;
            Relations = relations;
            PropertyValues = System.Array.Empty<ValueReference>();
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
            return MemoryScope.GetLocal(name);
        }

        /// <summary>O(1) property access by pre-computed index.</summary>
        public WarValue GetPropertyByIndex(int index)
        {
            return PropertyValues[index].Value;
        }

        public void SetProperty(string name, in WarValue value)
        {
            MemoryScope.SetLocal(name, value);
        }

        /// <summary>O(1) property write by pre-computed index.</summary>
        public void SetPropertyByIndex(int index, in WarValue value)
        {
            PropertyValues[index].Value = value;
        }

        public bool StructuralEquals(ClassData other)
        {
            WarValue.EqualityMemo? memo = null;
            return StructuralEquals(other, 0, ref memo, out _);
        }

        /// <summary>
        /// Structural equality of an instance nested <paramref name="depth"/>
        /// arrays or instances deep: its properties are compared one level
        /// deeper, so the <see cref="WarValue.MaxValueDepth"/> guard of
        /// <see cref="WarValue"/> equality applies (T1.2; DEC-9 removes
        /// structural class equality in T3.6). <paramref name="memo"/> and
        /// <paramref name="height"/> are as in
        /// <see cref="WarValue.Equals(in WarValue, int, ref WarValue.EqualityMemo?, out int)"/>.
        /// </summary>
        internal bool StructuralEquals(ClassData other, int depth, ref WarValue.EqualityMemo? memo, out int height)
        {
            height = 1;
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            if (memo != null && memo.TryGet(this, other, depth, out height)) return true;

            var properties = Definition.ClassDetails.Properties;
            var below = 0;
            for (int i = 0; i < properties.Count; i++)
            {
                var prop = properties[i];
                var thisVal = MemoryScope.GetLocal(prop);
                var otherVal = other.MemoryScope.GetLocal(prop);
                if (!thisVal.Equals(otherVal, depth + 1, ref memo, out var h))
                    return false;
                if (h > below) below = h;
            }
            height = below + 1;
            if (below > 0) WarValue.EqualityMemo.Add(ref memo, this, other, height);
            return true;
        }

        public int StructuralHashCode() => StructuralHashCode(0);

        /// <summary>
        /// Hash of an instance nested <paramref name="depth"/> deep: at most
        /// its first 8 properties, hashed one level deeper (see
        /// <see cref="WarValue.GetHashCode()"/>).
        /// </summary>
        internal int StructuralHashCode(int depth)
        {
            var hash = 17;
            var properties = Definition.ClassDetails.Properties;
            var n = System.Math.Min(properties.Count, 8);
            for (int i = 0; i < n; i++)
            {
                var val = MemoryScope.GetLocal(properties[i]);
                hash = hash * 31 + val.GetHashCode(depth + 1);
            }
            return hash;
        }
    }
}
