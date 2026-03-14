using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    /// <summary>
    /// Cast a class instance from one type to another
    /// </summary>
    public class ClassCastOperator : BinaryOperatorExpression
    {
        public ClassCastOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // evaluate expressions
            var classInstance = (ClassValue)left;
            var typeToCastName = ((VariableExpression)Right).Name;

            // retrieve class details
            var classDetails = classInstance.GetValue().ClassDetails;

            // check if the type to cast is different from original
            if (classDetails.Name == typeToCastName)
                return classInstance;

            // retrieve ClassValue of other type
            return classInstance.GetRelation(typeToCastName);
        }
    }
}