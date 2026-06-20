using WarScript.Expression;

namespace WarScript.Statement
{
    public class PrintStatement : Statement
    {
        public readonly IExpression Expression;

        public PrintStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            Expression = expression;
        }

        public override void Execute()
        {
            var value = Expression.Evaluate();
            if (_script.HaltFlags == 0)
            {
                _script.Logger?.Invoke(_script, value.ToString());
            }
            _script.ExceptionContext.AddTracedStatement(this);
        }
    }
}
