using System;
using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class MathLibrary
    {
        private static readonly Random Rng = new();

        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            // ── Powers & roots ──

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("pow", new List<string> { "base", "exp" }),
                args => WarValue.FromNumeric(Math.Pow(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns base raised to the power of exp.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sqrt", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Sqrt(NativeHelper.NumericArg(args, 0))),
                "Returns the square root of n.", "NumericValue"));

            // ── Rounding ──

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

            // ── Absolute, sign, clamp ──

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

            // ── Interpolation ──

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

            // ── Trigonometry ──

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sin", new List<string> { "radians" }),
                args => WarValue.FromNumeric(Math.Sin(NativeHelper.NumericArg(args, 0))),
                "Returns the sine of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("cos", new List<string> { "radians" }),
                args => WarValue.FromNumeric(Math.Cos(NativeHelper.NumericArg(args, 0))),
                "Returns the cosine of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("tan", new List<string> { "radians" }),
                args => WarValue.FromNumeric(Math.Tan(NativeHelper.NumericArg(args, 0))),
                "Returns the tangent of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("asin", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Asin(NativeHelper.NumericArg(args, 0))),
                "Returns the arc sine in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("acos", new List<string> { "n" }),
                args => WarValue.FromNumeric(Math.Acos(NativeHelper.NumericArg(args, 0))),
                "Returns the arc cosine in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("atan2", new List<string> { "y", "x" }),
                args => WarValue.FromNumeric(Math.Atan2(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the angle in radians between the x-axis and the point (x, y).", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("deg_to_rad", new List<string> { "degrees" }),
                args => WarValue.FromNumeric(NativeHelper.NumericArg(args, 0) * Math.PI / 180.0),
                "Converts degrees to radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("rad_to_deg", new List<string> { "radians" }),
                args => WarValue.FromNumeric(NativeHelper.NumericArg(args, 0) * 180.0 / Math.PI),
                "Converts radians to degrees.", "NumericValue"));

            // ── Constants ──

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("pi", new List<string>()),
                _ => WarValue.FromNumeric(Math.PI),
                "Returns pi (3.14159...).", "NumericValue"));

            // ── Random ──

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("random", new List<string> { "max" }),
                args => WarValue.FromNumeric(Rng.NextDouble() * NativeHelper.NumericArg(args, 0)),
                "Returns a random number between 0 and max (exclusive).", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("random_range", new List<string> { "min", "max" }),
                args =>
                {
                    var min = NativeHelper.NumericArg(args, 0);
                    var max = NativeHelper.NumericArg(args, 1);
                    return WarValue.FromNumeric(min + Rng.NextDouble() * (max - min));
                },
                "Returns a random number between min and max.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("random_int", new List<string> { "min", "max" }),
                args =>
                {
                    var min = (int)NativeHelper.NumericArg(args, 0);
                    var max = (int)NativeHelper.NumericArg(args, 1);
                    return WarValue.FromNumeric(Rng.Next(min, max + 1));
                },
                "Returns a random integer between min and max (inclusive).", "NumericValue"));
        }
    }
}