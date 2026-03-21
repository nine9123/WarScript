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
                args =>
                {
                    var b = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    var e = NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    return script.GetNumeric(Math.Pow(b, e));
                },
                "Returns base raised to the power of exp.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sqrt", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Sqrt(n));
                },
                "Returns the square root of n.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("floor", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Floor(n));
                },
                "Rounds down to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("ceil", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Ceiling(n));
                },
                "Rounds up to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("round", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Round(n));
                },
                "Rounds to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("abs", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Abs(n));
                },
                "Returns absolute value.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("min", new List<string> { "a", "b" }),
                args =>
                {
                    var a = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    var b = NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    return script.GetNumeric(Math.Min(a, b));
                },
                "Returns the smaller of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("max", new List<string> { "a", "b" }),
                args =>
                {
                    var a = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    var b = NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    return script.GetNumeric(Math.Max(a, b));
                },
                "Returns the larger of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("clamp", new List<string> { "n", "lo", "hi" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    var lo = NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    var hi = NativeHelper.Arg<NumericValue>(args, 2).GetValue();
                    return script.GetNumeric(Math.Max(lo, Math.Min(hi, n)));
                },
                "Clamps n between lo and hi.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sign", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    return script.GetNumeric(Math.Sign(n));
                },
                "Returns -1, 0, or 1.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("lerp", new List<string> { "a", "b", "t" }),
                args =>
                {
                    var a = NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    var b = NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    var t = NativeHelper.Arg<NumericValue>(args, 2).GetValue();
                    return script.GetNumeric(a + (b - a) * t);
                },
                "Linear interpolation from a to b by t.", "NumericValue"));
        }
    }
}