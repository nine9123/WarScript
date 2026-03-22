using System;
using System.Collections.Generic;
using System.Text;
using WarScript.Expression.Value;

namespace WarScript.Exception
{
    public class Exception
    {
        public readonly WarValue Value;
        public readonly List<Statement.Statement> StackTrace;

        public Exception(WarValue value, List<Statement.Statement> stackTrace)
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
