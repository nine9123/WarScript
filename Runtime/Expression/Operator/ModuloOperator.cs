using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ModuloOperator : BinaryOperatorExpression
    {
        public ModuloOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;
            var right = Right.Evaluate();
            if (right == null) return null;

            if (left is NumericValue leftNum && right is NumericValue rightNum)
                return new NumericValue(_script, leftNum.GetValue() % rightNum.GetValue());

            return _script.ExceptionContext.RaiseException($"Unable to perform modulo for non numeric values `{left}` and `{right}`");
        }
    }
}