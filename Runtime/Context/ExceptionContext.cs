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

        /// <summary>Raises a runtime error of a known kind (plan T2.3; every raise gets one in T2.5).</summary>
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
        /// The last exception reported (logged and cleared) as unhandled.
        /// Lets the golden test runner tell an unhandled error apart from
        /// printed output until the v2 result types exist.
        /// </summary>
        internal Exception.Exception LastUnhandled { get; private set; }

        private enum State { None, Raised, Disabled }
    }
}
