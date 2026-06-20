using WarScript.Expression;
using WarScript.Expression.Value;
using FixMath;

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

        public override void Execute()
        {
            F64 waitDuration = F64.Zero;
            if (YieldType == YieldType.Wait && Expression != null)
            {
                var val = Expression.Evaluate();
                if (val.IsNumeric) waitDuration = val.Numeric;
            }
            _script.SetYielded(YieldType, waitDuration);
        }
    }
}
