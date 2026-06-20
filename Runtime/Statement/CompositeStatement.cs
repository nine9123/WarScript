#nullable enable

using System.Collections.Generic;
using WarScript.Context;

namespace WarScript.Statement
{
    public class CompositeStatement : Statement
    {
        public readonly List<Statement> StatementsToExecute = new List<Statement>();

        public CompositeStatement(WarScriptLanguage script, int? rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }
        
        public void AddStatement(Statement? statement)
        {
            if (statement != null)
                StatementsToExecute.Add(statement);
        }

        public override void Execute()
        {
            var stmts = StatementsToExecute;
            for (int i = 0; i < stmts.Count; i++)
            {
                stmts[i].Execute();
                
                // Single flag check instead of 3 separate property lookups
                if (_script.HaltFlags != 0)
                    return;
            }
        }
    }
}