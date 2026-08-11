using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;

namespace Tests
{
    /// <summary>
    /// Covers standard-library surface that had no tests at all:
    ///   - MathLibrary: tan, asin, acos, atan2, deg_to_rad, rad_to_deg, and
    ///     edge inputs of sqrt/pow/clamp/lerp/sign
    ///   - CoroutineLibrary: the script-side functions (start_coroutine,
    ///     start_coroutine_loop, stop_coroutine, stop_all_coroutines) — only
    ///     the C# host API was exercised before
    ///   - NativeHelper argument validation as seen from a script (wrong type
    ///     and wrong arity are catchable script errors, not host crashes)
    /// </summary>
    [TestFixture]
    public class StdlibCoverageTests
    {
        private static (WarScriptLanguage script, List<string> output) RunWithLibs(string source)
        {
            return TestHelper.Run("stdlib", source,
                setupScope: (s, scope) => WarScriptLibraryRegistry.RegisterAll(s, scope));
        }

        private static List<string> Output(string source) => RunWithLibs(source).output;

        // ────────────────────────────────────────────────
        //  MathLibrary — previously untested functions.
        //  All transcendentals are fixed-point approximations, so results
        //  are asserted within a tolerance, in-script.
        // ────────────────────────────────────────────────

        [Test]
        public void Tan_OfZero_IsZero()
        {
            Assert.AreEqual("0", Output("print tan [0]")[0]);
        }

        [Test]
        public void Tan_OfQuarterPi_IsAboutOne()
        {
            Assert.AreEqual("True", Output("r = tan [pi [] / 4]\nprint abs [r - 1] < 0.001")[0]);
        }

        [Test]
        public void Asin_OfOne_IsAboutHalfPi()
        {
            Assert.AreEqual("True", Output("r = asin [1]\nprint abs [r - pi [] / 2] < 0.001")[0]);
        }

        [Test]
        public void Acos_OfOne_IsAboutZero()
        {
            Assert.AreEqual("True", Output("print abs [acos [1]] < 0.001")[0]);
        }

        [Test]
        public void Atan2_OneOne_IsAboutQuarterPi()
        {
            Assert.AreEqual("True", Output("r = atan2 [1, 1]\nprint abs [r - pi [] / 4] < 0.001")[0]);
        }

        [Test]
        public void Atan2_NegativeQuadrant()
        {
            // atan2(-1, -1) = -3π/4
            Assert.AreEqual("True",
                Output("r = atan2 [-1, -1]\nprint abs [r + 3 * pi [] / 4] < 0.001")[0]);
        }

        [Test]
        public void DegToRad_180_IsAboutPi()
        {
            Assert.AreEqual("True", Output("r = deg_to_rad [180]\nprint abs [r - pi []] < 0.001")[0]);
        }

        [Test]
        public void RadToDeg_Pi_IsAbout180()
        {
            Assert.AreEqual("True", Output("r = rad_to_deg [pi []]\nprint abs [r - 180] < 0.01")[0]);
        }

        [Test]
        public void DegToRad_RadToDeg_RoundTrips()
        {
            Assert.AreEqual("True",
                Output("r = rad_to_deg [deg_to_rad [90]]\nprint abs [r - 90] < 0.01")[0]);
        }

        // ── Edge inputs (pinned deterministic behavior) ──

        [Test]
        public void Sqrt_OfNegative_IsZero()
        {
            // FixPointCS clamps instead of throwing — deterministic and safe.
            Assert.AreEqual("0", Output("print sqrt [-4]")[0]);
        }

        [Test]
        public void Pow_NegativeBaseFractionalExponent_IsZero()
        {
            Assert.AreEqual("0", Output("print pow [-2, 0.5]")[0]);
        }

        [Test]
        public void Clamp_WithInvertedBounds_ReturnsHi()
        {
            // clamp[v, lo, hi] with lo > hi resolves min/max in fixed order.
            Assert.AreEqual("0", Output("print clamp [5, 10, 0]")[0]);
        }

        [Test]
        public void Lerp_DoesNotClampT()
        {
            Assert.AreEqual("20", Output("print lerp [0, 10, 2]")[0]);
        }

        [Test]
        public void Sign_OfZero_IsZero()
        {
            Assert.AreEqual("0", Output("print sign [0]")[0]);
        }

        [Test]
        public void MinMax_WithNegatives()
        {
            var output = Output("print min [3, -2]\nprint max [3, -2]");
            Assert.AreEqual(new[] { "-2", "3" }, output);
        }

        // ────────────────────────────────────────────────
        //  Native argument validation, seen from script
        // ────────────────────────────────────────────────

        [Test]
        public void NativeCall_WrongArgumentType_IsCatchable()
        {
            var output = Output("begin\nprint sqrt [\"x\"]\nrescue e\nprint e\nend");
            Assert.AreEqual("Native function 'sqrt' failed: Argument 0 expected Numeric, got Text", output[0]);
        }

        [Test]
        public void NativeCall_ArrayFunctionOnNumber_IsCatchable()
        {
            var output = Output("begin\nprint Array_length [5]\nrescue e\nprint e\nend");
            Assert.AreEqual(
                "Native function 'Array_length' failed: Argument 0 expected Array, got Numeric",
                output[0]);
        }

        [Test]
        public void NativeCall_WrongArity_IsUndefinedFunction()
        {
            // Lookup is by (name, argCount), so a wrong arity fails resolution
            // before the native body ever runs.
            var output = Output("begin\nprint sqrt []\nrescue e\nprint e\nend");
            Assert.AreEqual("Function 'sqrt' with 0 args is not defined", output[0]);
        }

        [Test]
        public void IsNull_OnNullAndNonNull()
        {
            var output = Output("print is_null [null]\nprint is_null [0]");
            Assert.AreEqual(new[] { "True", "False" }, output);
        }

        // ────────────────────────────────────────────────
        //  CoroutineLibrary — script-side functions
        // ────────────────────────────────────────────────

        [Test]
        public void StartCoroutine_FromScript_RunsToFirstYieldImmediately()
        {
            var (script, output) = RunWithLibs(
                "fun worker [a]\n" +
                "print \"w\" + a\n" +
                "yield\n" +
                "print \"done\"\n" +
                "end\n" +
                "id = start_coroutine [\"worker\", {5}]\n" +
                "print \"id=\" + id");

            // Body runs to the first yield during start_coroutine itself.
            Assert.AreEqual(new[] { "w5", "id=1" }, output);

            script.TickCoroutines(F64.FromInt(1));
            Assert.AreEqual("done", output[2]);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void StopCoroutine_FromScript_ById()
        {
            var (script, output) = RunWithLibs(
                "fun worker []\n" +
                "loop true\n" +
                "print \"tick\"\n" +
                "yield\n" +
                "end\n" +
                "end\n" +
                "id = start_coroutine [\"worker\", {}]\n" +
                "stop_coroutine [id]");

            script.TickCoroutines(F64.FromInt(1));
            script.TickCoroutines(F64.FromInt(1));

            // Only the initial run-to-first-yield printed; the stop took effect.
            Assert.AreEqual(new[] { "tick" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void StopAllCoroutines_FromScript()
        {
            var (script, output) = RunWithLibs(
                "fun worker [tag]\n" +
                "loop true\n" +
                "print tag\n" +
                "yield\n" +
                "end\n" +
                "end\n" +
                "start_coroutine [\"worker\", {\"a\"}]\n" +
                "start_coroutine [\"worker\", {\"b\"}]\n" +
                "stop_all_coroutines []");

            script.TickCoroutines(F64.FromInt(1));

            Assert.AreEqual(new[] { "a", "b" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void StartCoroutineLoop_RestartsAfterCompletion()
        {
            var (script, output) = RunWithLibs(
                "fun worker []\n" +
                "print \"run\"\n" +
                "yield\n" +
                "end\n" +
                "start_coroutine_loop [\"worker\", {}]");

            script.TickCoroutines(F64.FromInt(1)); // finishes first run, restarts
            script.TickCoroutines(F64.FromInt(1));

            Assert.GreaterOrEqual(output.Count, 2);
            foreach (var line in output)
                Assert.AreEqual("run", line);
            Assert.AreEqual(1, script.ActiveCoroutineCount);
        }

        [Test]
        public void StartCoroutine_MissingFunction_IsScriptError()
        {
            var (script, output) = RunWithLibs(
                "start_coroutine [\"nope\", {}]\nprint \"after\"");

            StringAssert.Contains("Coroutine function 'nope' with 0 arguments is not defined", output[0]);
            Assert.IsFalse(output.Contains("after"));
        }
    }
}
