using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class SubtractionOperator : BinaryOperatorExpression
    {
        public SubtractionOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right)
        {
            _script = script;
        }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left == _script.Null || right == _script.Null)
                return _script.ExceptionContext.RaiseException($"Unable to perform subtraction for NULL values `{left}`, `{right}`");

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return _script.GetNumeric(leftNum.GetValue() - rightNum.GetValue());

            return new TextValue(_script, left.ToString().Replace(right.ToString(), ""));
        }
    }
}