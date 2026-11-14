using System;
using System.Collections.Generic;
using FixMath;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace WarScript
{
    /// <summary>
    /// Argument extraction for native functions. A failed check throws
    /// <see cref="NativeArgumentException"/> (plan 6.9, T2.5): ArgumentError
    /// when the argument is missing, TypeError when it has the wrong type.
    /// </summary>
    public static class NativeHelper
    {
        public static WarValue Arg(List<WarValue> args, int index, ValueTag expectedTag)
        {
            if (index >= args.Count)
                throw new NativeArgumentException(ScriptErrorKind.ArgumentError,
                    System.FormattableString.Invariant($"Expected argument at position {index}, but only {args.Count} were provided"));

            var val = args[index];
            if (val.Tag != expectedTag)
                throw new NativeArgumentException(ScriptErrorKind.TypeError,
                    System.FormattableString.Invariant($"Argument {index} expected {expectedTag}, got {val.Tag}"));

            return val;
        }

        public static F64 NumericArg(List<WarValue> args, int index)
        {
            return Arg(args, index, ValueTag.Numeric).Numeric;
        }

        /// <summary>Numeric argument truncated to int (toward zero, matching D1).</summary>
        public static int IntArg(List<WarValue> args, int index)
        {
            return WarValue.ToInt(Arg(args, index, ValueTag.Numeric).Numeric);
        }

        public static WarValue ArrayArg(List<WarValue> args, int index)
        {
            return Arg(args, index, ValueTag.Array);
        }

        public static string TextArg(List<WarValue> args, int index)
        {
            return Arg(args, index, ValueTag.Text).TextValue;
        }

        public static T NativeArg<T>(List<WarValue> args, int index)
        {
            if (index >= args.Count)
                throw new NativeArgumentException(ScriptErrorKind.ArgumentError,
                    System.FormattableString.Invariant($"Expected argument at position {index}, but only {args.Count} were provided"));

            if (args[index].IsNativeObject && args[index].Ref is T typed)
                return typed;

            throw new NativeArgumentException(ScriptErrorKind.TypeError,
                System.FormattableString.Invariant($"Argument {index} expected {typeof(T).Name}, got {args[index].Tag}"));
        }
    }
}
