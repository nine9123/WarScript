using System;
using System.Collections.Generic;
using System.Text;
using WarScript.Expression.Value;

namespace WarScript.Exception
{
    /// <summary>
    /// Raised error
    /// </summary>
    public class Exception
    {
        /// <summary>
        /// Raised error
        /// </summary>
        public readonly IValue Value;

        /// <summary>
        /// Statements containing records of the application's movement leading to the statement that initiated the exception
        /// </summary>
        public readonly List<Statement.Statement> StackTrace;

        public Exception(IValue value, List<Statement.Statement> stackTrace)
        {
            Value = value;
            StackTrace = stackTrace;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.Append(Value);
            builder.Append(Environment.NewLine);

            for (var i = 0; i < StackTrace.Count; i++)
            {
                var trace = StackTrace[i];

                builder.AppendFormat("{0}at {1}:{2}", new string(' ', 4), trace.BlockName, trace.RowNumber);

                if (i < StackTrace.Count - 1)
                    builder.Append('\n');
            }

            return builder.ToString();
        }
    }
}