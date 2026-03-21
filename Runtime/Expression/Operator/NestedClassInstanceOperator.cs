using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class NestedClassInstanceOperator : BinaryOperatorExpression
    {
        public NestedClassInstanceOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // access class's property via this instance
            // this :: new NestedClass []
            if (left is ThisValue thisValue)
                left = thisValue.GetValue();

            if (left is ClassValue classInstance && Right is ClassExpression classExpr)
                // instantiate nested class
                // new Class [] :: new NestedClass []
                return classExpr.Evaluate(classInstance);

            return _script.ExceptionContext.RaiseException($"Unable to access class's nested class `{Right}`");
        }
    }
}