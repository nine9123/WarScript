using WarScript.Expression;

namespace WarScript.Statement.Loop
{
    public class WhileLoopStatement : AbstractLoopStatement
    {
        private readonly IExpression _hasNext;

        // Compiler accessor
        internal IExpression Condition => _hasNext;

        public WhileLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression hasNext) : base(script, rowNumber, blockName)
        {
            _hasNext = hasNext;
        }
    }
}
