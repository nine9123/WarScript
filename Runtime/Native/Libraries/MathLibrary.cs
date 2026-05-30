using System.Collections.Generic;
using FixMath;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class MathLibrary
    {
        // NOTE (D6): random / random_range / random_int were REMOVED. They were
        // backed by System.Random (time-seeded, non-deterministic) and are a
        // lockstep desync source. Deterministic randomness will be provided by the
        // engine's MasterRandom, exposed as a separate module. No script currently
        // depends on the old native random functions.

        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            // -- Powers & roots --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("pow", new List<string> { "base", "exp" }),
                args => WarValue.FromNumeric(F64.Pow(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns base raised to the power of exp.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sqrt", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Sqrt(NativeHelper.NumericArg(args, 0))),
                "Returns the square root of n.", "NumericValue"));

            // -- Rounding --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("floor", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Floor(NativeHelper.NumericArg(args, 0))),
                "Rounds down to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("ceil", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Ceil(NativeHelper.NumericArg(args, 0))),
                "Rounds up to nearest integer.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("round", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Round(NativeHelper.NumericArg(args, 0))),
                "Rounds to nearest integer.", "NumericValue"));

            // -- Absolute, sign, clamp --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("abs", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Abs(NativeHelper.NumericArg(args, 0))),
                "Returns absolute value.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("min", new List<string> { "a", "b" }),
                args => WarValue.FromNumeric(F64.Min(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the smaller of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("max", new List<string> { "a", "b" }),
                args => WarValue.FromNumeric(F64.Max(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the larger of two values.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("clamp", new List<string> { "n", "lo", "hi" }),
                args =>
                {
                    var n = NativeHelper.NumericArg(args, 0);
                    var lo = NativeHelper.NumericArg(args, 1);
                    var hi = NativeHelper.NumericArg(args, 2);
                    return WarValue.FromNumeric(F64.Clamp(n, lo, hi));
                },
                "Clamps n between lo and hi.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sign", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Sign(NativeHelper.NumericArg(args, 0))),
                "Returns -1, 0, or 1.", "NumericValue"));

            // -- Interpolation --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("lerp", new List<string> { "a", "b", "t" }),
                args =>
                {
                    var a = NativeHelper.NumericArg(args, 0);
                    var b = NativeHelper.NumericArg(args, 1);
                    var t = NativeHelper.NumericArg(args, 2);
                    return WarValue.FromNumeric(F64.Lerp(a, b, t));
                },
                "Linear interpolation from a to b by t.", "NumericValue"));

            // -- Trigonometry (radians) --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("sin", new List<string> { "radians" }),
                args => WarValue.FromNumeric(F64.Sin(NativeHelper.NumericArg(args, 0))),
                "Returns the sine of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("cos", new List<string> { "radians" }),
                args => WarValue.FromNumeric(F64.Cos(NativeHelper.NumericArg(args, 0))),
                "Returns the cosine of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("tan", new List<string> { "radians" }),
                args => WarValue.FromNumeric(F64.Tan(NativeHelper.NumericArg(args, 0))),
                "Returns the tangent of an angle in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("asin", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Asin(NativeHelper.NumericArg(args, 0))),
                "Returns the arc sine in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("acos", new List<string> { "n" }),
                args => WarValue.FromNumeric(F64.Acos(NativeHelper.NumericArg(args, 0))),
                "Returns the arc cosine in radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("atan2", new List<string> { "y", "x" }),
                args => WarValue.FromNumeric(F64.Atan2(NativeHelper.NumericArg(args, 0), NativeHelper.NumericArg(args, 1))),
                "Returns the angle in radians between the x-axis and the point (x, y).", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("deg_to_rad", new List<string> { "degrees" }),
                args => WarValue.FromNumeric(F64.DegToRad(NativeHelper.NumericArg(args, 0))),
                "Converts degrees to radians.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("rad_to_deg", new List<string> { "radians" }),
                args => WarValue.FromNumeric(F64.RadToDeg(NativeHelper.NumericArg(args, 0))),
                "Converts radians to degrees.", "NumericValue"));

            // -- Constants --
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("pi", new List<string>()),
                _ => WarValue.FromNumeric(F64.Pi),
                "Returns pi (3.14159...).", "NumericValue"));
        }
    }
}
