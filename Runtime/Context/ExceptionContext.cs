using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    public class ExceptionContext
    {
        public Exception.Exception Exception { get; private set; }
        private State _state = State.None;
        private readonly WarScriptLanguage _script;

        public ExceptionContext(WarScriptLanguage script)
        {
            _script = script;
        }

        public WarValue RaiseException(in WarValue value)
        {
            Exception = new Exception.Exception(value, new List<Statement.Statement>());
            _state = State.Raised;
            _script.HaltFlags |= WarScriptLanguage.HaltFlag.Exception;
            return default;
        }

        public WarValue RaiseException(string textValue)
        {
            return RaiseException(WarValue.FromText(textValue));
        }

        public void RescueException()
        {
            Exception = null;
            _state = State.None;
            _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Exception;
        }

        public void Disable() { _state = State.Disabled; }
        public void Enable() { _state = State.Raised; }
        public bool IsRaised() { return _state == State.Raised; }

        public void AddTracedStatement(Statement.Statement statement)
        {
            if (IsRaised())
                Exception.StackTrace.Add(statement);
        }

        public void PrintStackTrace()
        {
            LastUnhandled = Exception;
            _script.Logger?.Invoke(_script, Exception.ToString());
            RescueException();
        }

        /// <summary>
        /// The last exception reported (logged and cleared) as unhandled.
        /// Lets the golden test runner tell an unhandled error apart from
        /// printed output until the v2 result types exist.
        /// </summary>
        internal Exception.Exception LastUnhandled { get; private set; }

        private enum State { None, Raised, Disabled }
    }
}
