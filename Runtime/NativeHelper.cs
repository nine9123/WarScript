using System;
using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript
{
    public static class NativeHelper
    {
        public static T Arg<T>(List<IValue> args, int index) where T : IValue
        {
            if (index >= args.Count)
                throw new System.ArgumentException(
                    $"Expected argument at position {index}, but only {args.Count} were provided");

            if (args[index] is T typed)
                return typed;

            throw new System.ArgumentException(
                $"Argument {index} expected {typeof(T).Name}, got {args[index].GetType().Name}");
        }
        
        public static T NativeArg<T>(List<IValue> args, int index)
        {
            if (index >= args.Count)
                throw new ArgumentException(
                    $"Expected argument at position {index}, but only {args.Count} were provided");

            if (args[index] is INativeObjectValue native && native.GetRawValue() is T typed)
                return typed;

            throw new ArgumentException(
                $"Argument {index} expected {typeof(T).Name}, got {args[index].GetType().Name}");
        }
    }
}