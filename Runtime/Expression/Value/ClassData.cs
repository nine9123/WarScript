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

        /// <summary>
        /// The instance whose script created this instance (plan 6.7, Q-1,
        /// T3.5): its fields may be read and written from any instance, but
        /// calling one of its methods (or instantiating one of its nested
        /// classes) from another instance raises TypeError "belongs to
        /// another script instance". Null for an instance the host built
        /// (such as a generated <c>[WsEnum]</c>), which any instance may use.
        /// </summary>
        public readonly WarScriptLanguage? Owner;

        /// <summary>
        /// The instance this class part belongs to: the part of the class
        /// that was instantiated (the most derived one), which is its own
        /// root. A cast (<c>d as Base</c>) gives the base part, and
        /// <c>==</c> compares roots, so <c>d == (d as Base)</c> is true and
        /// two instances with equal fields are not equal (plan 5.4, DEC-9,
        /// PROP-8, T3.6). Set by <c>WarVM.AllocateInstance</c> for every base
        /// part; any other instance is its own root.
        /// </summary>
        public ClassData Root { get; internal set; }

        public ClassData(ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassData> relations)
            : this(definition, memoryScope, relations, null)
        {
        }

        public ClassData(ClassDefinition definition, MemoryScope memoryScope, Dictionary<string, ClassData> relations,
            WarScriptLanguage? owner)
        {
            Definition = definition;
            MemoryScope = memoryScope;
            Relations = relations;
            PropertyValues = System.Array.Empty<ValueReference>();
            Owner = owner;
            Root = this;
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
    }
}
