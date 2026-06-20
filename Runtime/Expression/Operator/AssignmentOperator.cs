using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class AssignmentOperator : BinaryOperatorExpression
    {
        public AssignmentOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (Left is IAssignExpression assignable)
                return assignable.Assign(right);

            return _script.RaiseException($"Unable to make an assignment for '{Left}'");
        }
    }
}
