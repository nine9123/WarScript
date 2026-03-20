using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public enum YieldType
    {
        NextTick,
        Wait,
        Until
    }

    public class YieldStatement : Statement
    {
        public readonly YieldType YieldType;
        public readonly IExpression Expression;

        public YieldStatement(
            WarScriptLanguage script,
            int rowNumber,
            string blockName,
            YieldType yieldType,
            IExpression expression)
            : base(script, rowNumber, blockName)
        {
            YieldType = yieldType;
            Expression = expression;
        }

        public override void Execute()
        {
            double waitDuration = 0;
            if (YieldType == YieldType.Wait && Expression != null)
            {
                var val = Expression.Evaluate();
                if (val is NumericValue num)
                    waitDuration = num.GetValue();
            }

            _script.SetYielded(YieldType, waitDuration, Expression);
        }
    }
}