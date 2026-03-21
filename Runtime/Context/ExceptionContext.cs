using System;
using System.Collections;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Context
{
    /// <summary>
    /// Associates thrown <see cref="WarScript.Exception.Exception"/> with the current execution statement
    ///
    /// <see cref="Statement.RaiseExceptionStatement"/>
    /// <see cref="Statement.HandleExceptionStatement"/>
    /// </summary>
    public class ExceptionContext
    {
        /// <summary>
        /// Raised exception
        /// </summary>
        public Exception.Exception Exception { get; private set; }

        /// <summary>
        /// State of the exception
        /// </summary>
        private State _state = State.None;

        private readonly WarScriptLanguage _script;
        
        public ExceptionContext(WarScriptLanguage script)
        {
            _script = script;
        }
        
        /// <summary>
        /// Raise an exception
        /// </summary>
        /// <param name="value">raised value</param>
        /// <returns>null</returns>
        public IValue RaiseException(IValue value)
        {
            Exception = new Exception.Exception(value, new List<Statement.Statement>());
            _state = State.Raised;
            _script.HaltFlags |= WarScriptLanguage.HaltFlag.Exception;
            return null;
        }

        /// <summary>
        /// Raise an exception
        /// </summary>
        /// <param name="textValue">raised text value</param>
        /// <returns>null</returns>
        public IValue RaiseException(string textValue)
        {
            return RaiseException(new TextValue(_script, textValue));
        }

        /// <summary>
        /// Rescue the exception if it's been handled
        /// </summary>
        public void RescueException()
        {
            Exception = null;
            _state = State.None;
            _script.HaltFlags &= ~WarScriptLanguage.HaltFlag.Exception;
        }

        /// <summary>
        /// Disable collecting of the stack trace records before executing <b>ensure</b> block
        /// </summary>
        public void Disable()
        {
            _state = State.Disabled;
        }

        /// <summary>
        /// Enable collecting the stack trace after quiting the <b>ensure</b> block
        /// </summary>
        public void Enable()
        {
            _state = State.Raised;
        }

        /// <summary>
        /// If an exception's been raised
        /// </summary>
        /// <returns></returns>
        public bool IsRaised()
        {
            return _state == State.Raised;
        }

        /// <summary>
        /// Add record of the application's movement as a statement that initiated the exception
        /// </summary>
        /// <param name="statement"></param>
        public void AddTracedStatement(Statement.Statement statement)
        {
            if (IsRaised())
                Exception.StackTrace.Add(statement);
        }

        /// <summary>
        /// Print an exception
        /// </summary>
        public void PrintStackTrace()
        {
            _script.Logger?.Invoke(_script, Exception.ToString());
            RescueException();
        }
        
        /// <summary>
        /// States of the ExceptionContext
        /// </summary>
        private enum State
        {
            /// <summary>
            /// No exception raised or the exception is rescued
            /// </summary>
            None,
            
            /// <summary>
            /// The exception is raised
            /// </summary>
            Raised,
            
            /// <summary>
            /// The exception disabled to execute <b>ensure</b> block
            /// </summary>
            Disabled
        }
    }
}