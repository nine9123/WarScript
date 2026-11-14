#nullable enable

using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Wrapper for WarValue to keep the properties' relations between Base and Derived classes.
    /// When class B[b_val] : A[b_val], both scopes share the same ValueReference.
    /// Mutating one updates the other.
    /// </summary>
    public class ValueReference
    {
        public WarValue Value;

        private ValueReference(in WarValue value)
        {
            Value = value;
        }

        public static ValueReference InstanceOf(in WarValue value)
        {
            return new ValueReference(value);
        }
    }
}
