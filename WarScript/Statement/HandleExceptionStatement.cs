using System;
using WarScript.Context;

namespace WarScript.Statement
{
    public class HandleExceptionStatement : Statement
    {
        public readonly CompositeStatement BeginStatement;
        public readonly CompositeStatement RescueStatement;
        public readonly CompositeStatement EnsureStatement;
        public readonly string ErrorVariable;
        
        public HandleExceptionStatement(
            int rowNumber, 
            string blockName,
            CompositeStatement beginStatement,
            CompositeStatement rescueStatement,
            CompositeStatement ensureStatement,
            string errorVariable) : base(rowNumber, blockName)
        {
            BeginStatement = beginStatement;
            RescueStatement = rescueStatement;
            EnsureStatement = ensureStatement;
            ErrorVariable = errorVariable;
        }
        
        public override void Execute()
        {
            MemoryContext.PushScope(MemoryContext.NewScope());
            try
            {
                BeginStatement.Execute();
            }
            finally
            {
                MemoryContext.EndScope();
            }
            
            // Rescue block
            if (RescueStatement != null && ExceptionContext.IsRaised())
            {
                MemoryContext.PushScope(MemoryContext.NewScope());
                if (ErrorVariable != null)
                {
                    MemoryContext.GetScope().SetLocal(ErrorVariable, ExceptionContext.Exception.Value);
                }
                
                ExceptionContext.RescueException();

                try
                {
                    RescueStatement.Execute();
                }
                finally
                {
                    MemoryContext.EndScope();
                }
            }
            
            // Ensure block
            if (EnsureStatement != null)
            {
                var raised = ExceptionContext.IsRaised();
                if (raised)
                {
                    // Ensure block shouldn't accumulate stack trace
                    ExceptionContext.Disable();
                }
                
                MemoryContext.PushScope(MemoryContext.NewScope());
                try
                {
                    EnsureStatement.Execute();
                }
                finally
                {
                    MemoryContext.EndScope();
                    if (raised)
                    {
                        // Continue to accumulate stack trace
                        ExceptionContext.Enable();
                    }
                }
            }
        }
    }
}