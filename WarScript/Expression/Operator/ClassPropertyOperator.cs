using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ClassPropertyOperator : BinaryOperatorExpression, IAssignExpression
    {
        public ClassPropertyOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // access class's property via this instance
            // this :: class_argument
            if (left is ThisValue thisValue)
                left = thisValue.GetValue();

            if (left is ClassValue classInstance)
            {
                if (Right is VariableExpression varExpr)
                    // access class's property
                    // new Class [] :: class_property
                    return classInstance.GetValue(varExpr.Name);

                if (Right is FunctionExpression funcExpr)
                    // execute class's function
                    // new Class [] :: class_function []
                    return funcExpr.Evaluate(classInstance);
            }

            return ExceptionContext.RaiseException($"Unable to access class's property `{Right}`");
        }

        public IValue Assign(IValue value)
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // access class's property via this instance
            // this :: class_argument
            if (left is ThisValue thisValue)
                left = thisValue.GetValue();

            if (left is ClassValue classInstance && Right is VariableExpression varExpr)
            {
                var propertyName = varExpr.Name;
                classInstance.SetValue(propertyName, value);
            }

            return left;
        }
    }
}