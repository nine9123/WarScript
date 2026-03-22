using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public abstract class BinaryOperatorExpression : IOperatorExpression
    {
        public IExpression Left { get; private set; }
        public IExpression Right { get; private set; }
        protected WarScriptLanguage _script;

        protected BinaryOperatorExpression(WarScriptLanguage script, IExpression left, IExpression right)
        {
            _script = script;
            Left = left;
            Right = right;
        }

        public abstract WarValue Evaluate();
    }
}
