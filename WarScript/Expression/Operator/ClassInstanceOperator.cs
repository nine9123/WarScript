using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public class ClassInstanceOperator : UnaryOperatorExpression
    {
        public ClassInstanceOperator(IExpression value) : base(value) { }

        public override IValue Evaluate()
        {
            return Value.Evaluate(); // will return ToString() value
        }
    }
}