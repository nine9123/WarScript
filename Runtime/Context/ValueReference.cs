#nullable enable

using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Wrapper for the Value to keep the properties' relations between Base and Derived classes
    ///
    /// <code>
    /// # Declare the Base class A
    /// class A [a_value]
    /// end
    ///
    /// # Declare the Derived class B that inherits class A and initializes its `a_value` property with the `b_value` parameter
    /// class B [b_value]: A [b_value]
    /// end
    ///
    /// # Create an instance of class B
    /// b = new B [ b_value ]
    ///
    /// # If we change the `b_value` property, the A class's property `a_value` should be updated as well
    /// b :: b_value = new_value
    ///
    /// # a_new_value should contain `new_value`
    /// a_new_value = b as A :: a_value
    /// </code>
    /// </summary>
    public class ValueReference : IExpression
    {
        public IValue Value;

        private ValueReference(IValue value)
        {
            Value = value;
        }

        /// <summary>
        /// Evaluates Expression and creates ValueReference for it
        /// </summary>
        public static ValueReference? InstanceOf(IExpression? expression)
        {
            if (expression == null)
                return null;
            
            if (expression is ValueReference valueReference)
            {
                return valueReference;
            }
            else
            {
                var value = expression.Evaluate();
                if (value == null) return null;
                return new ValueReference(value);
            }
        }
        
        public IValue Evaluate()
        {
            return Value;
        }
    }
}