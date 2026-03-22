using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class WhileLoopStatement : AbstractLoopStatement
    {
        private readonly IExpression _hasNext;

        public WhileLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression hasNext) : base(script, rowNumber, blockName)
        {
            _hasNext = hasNext;
        }

        protected override void Init() { }

        protected override bool HasNext()
        {
            var value = _hasNext.Evaluate();
            return value.IsLogical && value.LogicalValue;
        }

        protected override void PreIncrement() { }
        protected override void PostIncrement() { }
    }
}
