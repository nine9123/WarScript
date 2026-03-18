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
            foreach (var statement in StatementsToExecute)
            {
                statement.Execute();
                
                // Stop the execution in case Exception occurred
                if (_script.ExceptionContext.IsRaised())
                    return;

                // Stop the execution in case ReturnStatement is invoked
                if (_script.ReturnContext.GetScope().Invoked)
                    return;
            }
        }
    }
}