using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class DivisionOperator : BinaryOperatorExpression
    {
        public DivisionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return _script.GetNumeric(leftNum.GetValue() / rightNum.GetValue());

            return _script.ExceptionContext.RaiseException($"Unable to divide non numeric values `{left}` and `{right}`");
        }
    }
}