using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ClassInstanceOperator : UnaryOperatorExpression
    {
        public ClassInstanceOperator(WarScriptLanguage script, IExpression value) : base(script, value) { }

        public override IValue Evaluate()
        {
            return Value.Evaluate(); // will return ToString() value
        }
    }
}