using System;
using WarScript.Expression.Value;

namespace WarScript.Exception
{
    /// <summary>
    /// A raised script error: the value a <c>rescue</c> variable receives.
    /// Its call stack comes with the structured errors of plan T2.5.
    /// </summary>
    public class Exception
    {
        public readonly WarValue Value;

        public Exception(WarValue value)
        {
            Value = value;
        }

        // The text logged for an unhandled error: the value and a line break
        // (where the tree-walker's statement trace used to follow).
        public override string ToString() => Value + Environment.NewLine;
    }
}
