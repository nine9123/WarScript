#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Exception
{
    /// <summary>
    /// A raised script error: the value a <c>rescue</c> variable receives,
    /// with its kind and, once a VM has handled it, where it was raised
    /// (plan T2.5).
    /// </summary>
    public class Exception
    {
        public readonly WarValue Value;

        /// <summary>What kind of error it is, for the host's <see cref="ScriptError"/> (plan T2.5).</summary>
        internal ScriptErrorKind Kind { get; }

        /// <summary>
        /// The call stack, innermost frame first: the frames of the VM that
        /// raised it, then those of every VM it propagated into through a
        /// native call (a nested entry, plan 6.5). Null until a VM first
        /// handles the error (an error raised outside any frame has none).
        /// </summary>
        internal List<ScriptFrame>? CallStack { get; private set; }

        /// <summary>The innermost frame's script and line; null and 0 while there is no frame.</summary>
        internal string? ScriptName => CallStack != null && CallStack.Count > 0 ? CallStack[0].ScriptName : null;
        internal int Line => CallStack != null && CallStack.Count > 0 ? CallStack[0].Line : 0;

        /// <summary>The VM whose frames were added last, so a re-raise in the same VM adds none.</summary>
        internal object? StackOwner { get; private set; }

        public Exception(WarValue value) : this(value, ScriptErrorKind.Raised)
        {
        }

        internal Exception(WarValue value, ScriptErrorKind kind)
        {
            Value = value;
            Kind = kind;
        }

        /// <summary>Adds the frames of <paramref name="owner"/> (a VM) below those already recorded.</summary>
        internal List<ScriptFrame> BeginFrames(object owner)
        {
            StackOwner = owner;
            return CallStack ??= new List<ScriptFrame>();
        }

        // The text logged for an unhandled error: the value and a line break
        // (where the tree-walker's statement trace used to follow).
        public override string ToString() => Value + Environment.NewLine;
    }
}
