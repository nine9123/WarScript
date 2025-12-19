using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax.Statement
{
    public class ConditionStatement : CompositeStatement
    {
        public readonly IExpression Condition;

        public ConditionStatement(IExpression condition)
        {
            Condition = condition;
        }

        public override void Execute()
        {
            var value = Condition.Evaluate();
            if (value is LogicalValue logicalValue)
            {
                if (logicalValue.ValueField)
                {
                    base.Execute();
                }
            }
            else
            {
                throw new Exception($"Cannot compare non logical value {value}");
            }
        }
    }
}