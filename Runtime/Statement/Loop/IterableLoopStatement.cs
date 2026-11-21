using WarScript.Expression;

namespace WarScript.Statement.Loop
{
    public class IterableLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variableExpression;
        private readonly IExpression _iterableExpression;

        // Compiler accessors
        internal VariableExpression Variable => _variableExpression;
        internal IExpression Iterable => _iterableExpression;

        public IterableLoopStatement(WarScriptLanguage script, int rowNumber, string blockName,
            VariableExpression variableExpression, IExpression iterableExpression)
            : base(script, rowNumber, blockName)
        {
            _variableExpression = variableExpression;
            _iterableExpression = iterableExpression;
        }
    }
}
