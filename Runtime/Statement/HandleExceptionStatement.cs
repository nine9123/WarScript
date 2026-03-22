using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class HandleExceptionStatement : Statement
    {
        public readonly CompositeStatement BeginStatement;
        public readonly CompositeStatement RescueStatement;
        public readonly CompositeStatement EnsureStatement;
        public readonly string ErrorVariable;

        public HandleExceptionStatement(
            WarScriptLanguage script, int rowNumber, string blockName,
            CompositeStatement beginStatement,
            CompositeStatement rescueStatement,
            CompositeStatement ensureStatement,
            string errorVariable) : base(script, rowNumber, blockName)
        {
            BeginStatement = beginStatement;
            RescueStatement = rescueStatement;
            EnsureStatement = ensureStatement;
            ErrorVariable = errorVariable;
        }

        public override void Execute()
        {
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
            try { BeginStatement.Execute(); }
            finally { _script.MemoryContext.EndScope(); }

            if (RescueStatement != null && _script.ExceptionContext.IsRaised())
            {
                _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
                if (ErrorVariable != null)
                    _script.MemoryContext.GetScope().SetLocal(ErrorVariable, _script.ExceptionContext.Exception.Value);
                _script.ExceptionContext.RescueException();
                try { RescueStatement.Execute(); }
                finally { _script.MemoryContext.EndScope(); }
            }

            if (EnsureStatement != null)
            {
                var raised = _script.ExceptionContext.IsRaised();
                if (raised) _script.ExceptionContext.Disable();
                var savedFlags = _script.HaltFlags;
                _script.HaltFlags = WarScriptLanguage.HaltFlag.None;
                _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
                try { EnsureStatement.Execute(); }
                finally
                {
                    _script.MemoryContext.EndScope();
                    _script.HaltFlags = savedFlags;
                    if (raised) _script.ExceptionContext.Enable();
                }
            }
        }
    }
}
