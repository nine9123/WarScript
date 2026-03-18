using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public abstract class AbstractLoopStatement : CompositeStatement
    {
        protected AbstractLoopStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }

        protected abstract void Init();

        protected abstract bool HasNext();

        protected abstract void PreIncrement();
        
        protected abstract void PostIncrement();

        public override void Execute()
        {
            // Memory scope for counter variables
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
            try
            {
                // Init loop
                Init();

                while (HasNext())
                {
                    PreIncrement();
                    
                    // Isolated memory scope for each iteration
                    _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

                    try
                    {
                        // Execute inner statements
                        foreach (var statement in StatementsToExecute)
                        {
                            statement.Execute();
                            
                            // Stop the execution in case Exception occurred
                            if (_script.ExceptionContext.IsRaised())
                                return;

                            // Stop the execution in case ReturnStatement is invoked
                            if (_script.ReturnContext.GetScope().Invoked)
                                return;

                            // Stop the execution in case BreakStatement is invoked
                            if (_script.BreakContext.GetScope().Invoked)
                                return;

                            // Jump to the next iteration in case NextStatement is invoked
                            if (_script.NextContext.GetScope().Invoked)
                                break;
                        }
                    }
                    finally
                    {
                        _script.NextContext.Reset();
                        // Release each iteration memory
                        _script.MemoryContext.EndScope();
                        
                        // Increment the counter even if the NextStatement is called
                        PostIncrement();
                    }
                }
            }
            finally
            {
                // Release loop memory
                _script.MemoryContext.EndScope();
                _script.BreakContext.Reset();
            }
        }
    }
}