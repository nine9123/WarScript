using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public abstract class AbstractLoopStatement : CompositeStatement
    {
        protected AbstractLoopStatement(int rowNumber, string blockName) : base(rowNumber, blockName)
        {
        }

        protected abstract void Init();

        protected abstract bool HasNext();

        protected abstract void PreIncrement();
        
        protected abstract void PostIncrement();

        public override void Execute()
        {
            // Memory scope for counter variables
            MemoryContext.PushScope(MemoryContext.NewScope());
            try
            {
                // Init loop
                Init();

                while (HasNext())
                {
                    PreIncrement();
                    
                    // Isolated memory scope for each iteration
                    MemoryContext.PushScope(MemoryContext.NewScope());

                    try
                    {
                        // Execute inner statements
                        foreach (var statement in StatementsToExecute)
                        {
                            statement.Execute();
                            
                            // Stop the execution in case Exception occurred
                            if (ExceptionContext.IsRaised())
                                return;

                            // Stop the execution in case ReturnStatement is invoked
                            if (ReturnContext.GetScope().Invoked)
                                return;

                            // Stop the execution in case BreakStatement is invoked
                            if (BreakContext.GetScope().Invoked)
                                return;

                            // Jump to the next iteration in case NextStatement is invoked
                            if (NextContext.GetScope().Invoked)
                                break;
                        }
                    }
                    finally
                    {
                        NextContext.Reset();
                        // Release each iteration memory
                        MemoryContext.EndScope();
                        
                        // Increment the counter even if the NextStatement is called
                        PostIncrement();
                    }
                }
            }
            finally
            {
                // Release loop memory
                MemoryContext.EndScope();
                BreakContext.Reset();
            }
        }
    }
}