using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class AssignmentOperator : BinaryOperatorExpression
    {
        public AssignmentOperator(IExpression left, IExpression right) : base(left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (Left is IAssignExpression assignable)
                return assignable.Assign(right);

            return ExceptionContext.RaiseException($"Unable to make an assignment for '{Left}'");
        }
    }
}