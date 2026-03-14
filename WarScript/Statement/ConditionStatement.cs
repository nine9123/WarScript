using System.Collections.Generic;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class ConditionStatement : Statement
    {
        public readonly List<KeyValuePair<IExpression, CompositeStatement>> Cases;
        
        public ConditionStatement(int rowNumber, string blockName) : base(rowNumber, blockName)
        {
            Cases = new List<KeyValuePair<IExpression, CompositeStatement>>();
        }

        public void AddCase(IExpression caseCondition, CompositeStatement caseStatement)
        {
            Cases.Add(new KeyValuePair<IExpression, CompositeStatement>(caseCondition, caseStatement));
        }
        
        public override void Execute()
        {
            foreach (var keyValuePair in Cases)
            {
                var condition = keyValuePair.Key;
                var value = condition.Evaluate();
                if (value is LogicalValue logicalValue && logicalValue.GetValue())
                {
                    MemoryContext.PushScope(MemoryContext.NewScope());
                    try
                    {
                        var statement = keyValuePair.Value;
                        statement.Execute();
                    }
                    finally
                    {
                        MemoryContext.EndScope();
                    }
                    break;
                }
            }
        }
    }
}