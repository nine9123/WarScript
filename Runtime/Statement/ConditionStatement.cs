using System.Collections.Generic;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class ConditionStatement : Statement
    {
        public readonly List<KeyValuePair<IExpression, CompositeStatement>> Cases;

        public ConditionStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
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
                var value = keyValuePair.Key.Evaluate();
                if (value.IsLogical && value.LogicalValue)
                {
                    _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
                    try
                    {
                        keyValuePair.Value.Execute();
                    }
                    finally
                    {
                        _script.MemoryContext.EndScope();
                    }
                    break;
                }
            }
        }
    }
}
