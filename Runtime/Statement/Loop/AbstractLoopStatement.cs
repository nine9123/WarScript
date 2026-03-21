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

                var stmts = StatementsToExecute;

                while (HasNext())
                {
                    PreIncrement();

                    // Isolated memory scope for each iteration (pooled via MemoryContext)
                    _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());

                    try
                    {
                        // Execute inner statements
                        for (int i = 0; i < stmts.Count; i++)
                        {
                            stmts[i].Execute();

                            if (_script.HaltFlags != 0)
                            {
                                // Check specific flags only when something is set
                                if ((_script.HaltFlags & (WarScriptLanguage.HaltFlag.Exception
                                    | WarScriptLanguage.HaltFlag.Return
                                    | WarScriptLanguage.HaltFlag.Break
                                    | WarScriptLanguage.HaltFlag.Yield)) != 0)
                                    return;

                                // Next: break inner loop, continue outer while
                                if ((_script.HaltFlags & WarScriptLanguage.HaltFlag.Next) != 0)
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        // Clear Next flag
                        _script.NextContext.Reset();
                        _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Next;
                        // Release each iteration memory (returns to pool)
                        _script.MemoryContext.EndScope();
                        
                        // Increment the counter even if the NextStatement is called
                        PostIncrement();
                    }
                    
                    // Break the outer while loop if yielded
                    if (_script.IsYielded)
                        break;
                }
            }
            finally
            {
                // Release loop memory
                _script.MemoryContext.EndScope();
                _script.BreakContext.Reset();
                _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Break;
            }
        }
    }
}