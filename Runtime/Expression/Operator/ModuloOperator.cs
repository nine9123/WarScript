using WarScript.Expression.Value;
using FixMath;

namespace WarScript.Expression.Operator
{
    public sealed class ModuloOperator : BinaryOperatorExpression
    {
        public ModuloOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsNumeric && right.IsNumeric)
            {
                if (right.Numeric == F64.Zero)
                    return _script.RaiseException("Modulo by zero");
                return WarValue.FromNumeric(left.Numeric % right.Numeric);
            }

            return _script.RaiseException($"Unable to perform modulo for non numeric values `{left}` and `{right}`");
        }
    }
}
