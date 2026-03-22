using System;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript
{
    public static class NativeHelper
    {
        public static WarValue Arg(List<WarValue> args, int index, ValueTag expectedTag)
        {
            if (index >= args.Count)
                throw new ArgumentException(
                    $"Expected argument at position {index}, but only {args.Count} were provided");

            var val = args[index];
            if (val.Tag != expectedTag)
                throw new ArgumentException(
                    $"Argument {index} expected {expectedTag}, got {val.Tag}");

            return val;
        }

        public static double NumericArg(List<WarValue> args, int index)
        {
            return Arg(args, index, ValueTag.Numeric).Numeric;
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
                throw new ArgumentException(
                    $"Expected argument at position {index}, but only {args.Count} were provided");

            if (args[index].IsNativeObject && args[index].Ref is T typed)
                return typed;

            throw new ArgumentException(
                $"Argument {index} expected {typeof(T).Name}, got {args[index].Tag}");
        }
    }
}
