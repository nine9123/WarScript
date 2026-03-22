using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class RaiseExceptionStatement : Statement
    {
        private readonly IExpression _expression;

        public RaiseExceptionStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            _expression = expression;
        }

        public override void Execute()
        {
            var value = _expression.Evaluate();
            if (_script.HaltFlags == 0)
            {
                if (value.IsNull)
                    value = WarValue.FromText("Empty exception");
                _script.ExceptionContext.RaiseException(value);
            }
            _script.ExceptionContext.AddTracedStatement(this);
        }
    }
}
