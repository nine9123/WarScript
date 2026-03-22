using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ArrayAppendOperator : BinaryOperatorExpression
    {
        public ArrayAppendOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;
            var right = Right.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsArray)
                left.ArrayAppend(right);

            return left;
        }
    }
}
