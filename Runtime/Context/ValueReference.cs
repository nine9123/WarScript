#nullable enable

using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Wrapper for WarValue to keep the properties' relations between Base and Derived classes.
    /// When class B[b_val] : A[b_val], both scopes share the same ValueReference.
    /// Mutating one updates the other.
    /// </summary>
    public class ValueReference : IExpression
    {
        public WarValue Value;

        private ValueReference(WarValue value)
        {
            Value = value;
        }

        public static ValueReference InstanceOf(WarValue value)
        {
            return new ValueReference(value);
        }

        public static ValueReference? InstanceOf(IExpression? expression)
        {
            if (expression == null)
                return null;

            if (expression is ValueReference valueReference)
                return valueReference;

            var value = expression.Evaluate();
            return new ValueReference(value);
        }

        public WarValue Evaluate()
        {
            return Value;
        }
    }
}
