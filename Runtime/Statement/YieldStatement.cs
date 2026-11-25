using WarScript.Expression;

namespace WarScript.Statement
{
    public enum YieldType { NextTick, Wait, Until }

    public class YieldStatement : Statement
    {
        public readonly YieldType YieldType;
        public readonly IExpression Expression;

        public YieldStatement(WarScriptLanguage script, int rowNumber, string blockName,
            YieldType yieldType, IExpression expression) : base(script, rowNumber, blockName)
        {
            YieldType = yieldType;
            Expression = expression;
        }
    }
}
