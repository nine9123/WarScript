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
            Exception = new Exception.Exception(value);
            _state = State.Raised;
            return default;
        }

        /// <summary>
        /// Raises an error again, keeping its kind and call stack: the error
        /// an ensure block re-raises when it ends (plan T2.5).
        /// </summary>
        internal void Reraise(Exception.Exception exception)
        {
            Exception = exception;
            _state = State.Raised;
        }

        /// <summary>Raises a runtime error of a given kind (plan T2.5).</summary>
        internal WarValue RaiseException(string textValue, ScriptErrorKind kind)
        {
            Exception = new Exception.Exception(WarValue.FromText(textValue), kind);
            _state = State.Raised;
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
        }

        public void Disable() { _state = State.Disabled; }
        public void Enable() { _state = State.Raised; }
        public bool IsRaised() { return _state == State.Raised; }

        public void PrintStackTrace()
        {
            LastUnhandled = Exception;
            _script.Logger?.Invoke(_script, Exception.ToString());
            RescueException();
        }

        /// <summary>
        /// Clears an error that escaped the outermost entry, keeping it as
        /// <see cref="LastUnhandled"/>; the entry's result carries it and the
        /// instance halts with it (plan T2.4), which logs it.
        /// </summary>
        internal void TakeUnhandled()
        {
            LastUnhandled = Exception;
            RescueException();
        }

        /// <summary>
        /// The last exception that escaped an outermost entry (cleared by
        /// <see cref="TakeUnhandled"/> or <see cref="PrintStackTrace"/>).
        /// </summary>
        internal Exception.Exception LastUnhandled { get; private set; }

        private enum State { None, Raised, Disabled }
    }
}
