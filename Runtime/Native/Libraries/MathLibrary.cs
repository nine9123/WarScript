using System;
using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class MathLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("pow", new List<string> { "base", "exp" }),
                args => WarValue.FromNumeric(Math.Pow(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns base raised to the power of exp.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sqrt", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Sqrt(NativeHelper.NumericArg(args, 0))),
                "Returns the square root of n.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("floor", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Floor(NativeHelper.NumericArg(args, 0))),
                "Rounds down to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("ceil", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Ceiling(NativeHelper.NumericArg(args, 0))),
                "Rounds up to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("round", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Round(NativeHelper.NumericArg(args, 0))),
                "Rounds to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("abs", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Abs(NativeHelper.NumericArg(args, 0))),
                "Returns absolute value.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("min", new List<string> { "a", "b" }),
                args => WarValue.FromNumeric(Math.Min(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the smaller of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("max", new List<string> { "a", "b" }),
                args => WarValue.FromNumeric(Math.Max(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the larger of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("clamp", new List<string> { "n", "lo", "hi" }),
                args =>
                {
                    var n = NativeHelper.NumericArg(args, 0);
                    var lo = NativeHelper.NumericArg(args, 1);
                    var hi = NativeHelper.NumericArg(args, 2);
                    return WarValue.FromNumeric(Math.Max(lo, Math.Min(hi, n)));
                },
                "Clamps n between lo and hi.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sign", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Sign(NativeHelper.NumericArg(args, 0))),
                "Returns -1, 0, or 1.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("lerp", new List<string> { "a", "b", "t" }),
                args =>
                {
                    var a = NativeHelper.NumericArg(args, 0);
                    var b = NativeHelper.NumericArg(args, 1);
                    var t = NativeHelper.NumericArg(args, 2);
                    return WarValue.FromNumeric(a + (b - a) * t);
                },
                "Linear interpolation from a to b by t.", "NumericValue"));
        }
    }
}
