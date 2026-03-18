using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ClassInstanceOfOperator : BinaryOperatorExpression
    {
        public ClassInstanceOfOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            if (left is ClassValue classInstance && Right is VariableExpression varExpr)
            {
                var classType = varExpr.Name;
                return new LogicalValue(_script, classInstance.ContainsRelation(classType));
            }

            return _script.ExceptionContext.RaiseException($"Unable to perform `is` operator for the following operands `{left}` and `{Right}`");
        }
    }
}