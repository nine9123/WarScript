#nullable enable

using System.Collections.Generic;

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
    }
}