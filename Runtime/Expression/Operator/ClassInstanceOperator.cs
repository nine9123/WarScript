using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ClassInstanceOperator : UnaryOperatorExpression
    {
        public ClassInstanceOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override WarValue Evaluate()
        {
            return Value.Evaluate();
        }
    }
}
