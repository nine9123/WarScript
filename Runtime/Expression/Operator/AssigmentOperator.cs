using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class AssignmentOperator : BinaryOperatorExpression
    {
        public AssignmentOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (Left is IAssignExpression assignable)
                return assignable.Assign(right);

            return _script.ExceptionContext.RaiseException($"Unable to make an assignment for '{Left}'");
        }
    }
}