using System.Collections.Generic;

namespace WarScript.Syntax.Statement
{
    public class CompositeStatement : IStatement
    {
        private readonly List<IStatement> _statementsToExecute = new List<IStatement>();

        public void AddStatement(IStatement statement)
        {
            if (statement != null)
                _statementsToExecute.Add(statement);
        }

        public virtual void Execute()
        {
            foreach (var statement in _statementsToExecute)
            {
                statement.Execute();
            }
        }
    }
}