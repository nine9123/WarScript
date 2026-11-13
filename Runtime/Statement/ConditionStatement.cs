using System.Collections.Generic;
using WarScript.Expression;

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
    }
}
