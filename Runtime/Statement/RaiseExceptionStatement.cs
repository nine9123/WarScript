using WarScript.Expression;

namespace WarScript.Statement
{
    public class RaiseExceptionStatement : Statement
    {
        private readonly IExpression _expression;

        // Compiler accessor
        internal IExpression Expression => _expression;

        public RaiseExceptionStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            _expression = expression;
        }
    }
}
