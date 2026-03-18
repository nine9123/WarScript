using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public abstract class UnaryOperatorExpression : IOperatorExpression
    {
        public IExpression Value { get; private set; }
        protected WarScriptLanguage _script;
        
        protected UnaryOperatorExpression(WarScriptLanguage script, IExpression value)
        {
            _script = script;
            Value = value;
        }

        public abstract IValue Evaluate();
    }
}