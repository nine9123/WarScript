using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ArrayValueOperator : BinaryOperatorExpression, IAssignExpression
    {
        public ArrayValueOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsArray && right.IsNumeric)
                return left.GetArrayElement(WarValue.ToInt(right.Numeric));
            if (left.IsText && right.IsNumeric)
                return left.GetTextChar(WarValue.ToInt(right.Numeric));

            return left;
        }

        public WarValue Assign(WarValue value)
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsArray && right.IsNumeric)
                left.SetArrayElement(WarValue.ToInt(right.Numeric), value);
            else if (left.IsText && right.IsNumeric)
            {
                var newText = left.SetTextChar(WarValue.ToInt(right.Numeric), value.ToString());
                if (Left is IAssignExpression assignable)
                    assignable.Assign(newText);
            }

            return left;
        }
    }
}
