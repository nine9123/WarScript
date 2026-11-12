using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Edge-case coverage for the deterministic 32.32 fixed-point (F64) migration.
    /// These exercise behavior that is specific to fixed-point and would NOT have
    /// held (or would have differed) under the old double-based numerics:
    ///   - fractional literal parsing &amp; exact representability
    ///   - F64→int truncation toward zero (array indices, repeat counts)
    ///   - round() being half-up (not banker's), floor/ceil on negatives
    ///   - catchable divide/modulo by zero (D4)
    ///   - transcendentals being approximate (asserted with tolerance)
    ///   - the integer-part representable range boundary
    /// All scripts are driven through TestHelper.Run with MathLibrary registered.
    /// </summary>
    [TestFixture]
    public class FixedPointMathTests
    {
        private static (WarScriptLanguage script, List<string> output) RunMath(string source)
        {
            return TestHelper.Run("fp_test.ws", source,
                (s, scope) => MathLibrary.Register(s, scope));
        }

        private static void AssertClean(WarScriptLanguage script, List<string> output)
        {
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script raised unexpectedly. Output:\n{string.Join("\n", output)}");
        }

        private static F64 Num(WarScriptLanguage s, string var) => s.UserMemoryScope.Get(var).Numeric;

        // ──────────────────────────────────────────────────────────
        //  Fractional literals & exact representability
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Literal_Half_IsExact()
        {
            // 0.5 == 1/2 is exactly representable in 32.32; equality must be exact.
            var (s, o) = RunMath("x = 0.5\nassert x == 0.5\nassert x + x == 1\n");
            AssertClean(s, o);
            Assert.AreEqual(F64.Half, Num(s, "x"));
        }

        [Test]
        public void Literal_NinetyNinePointFive_IsExact()
        {
            // 99.5 is exactly representable; round-trips through the store/getter exactly.
            var (s, o) = RunMath("x = 99.5\nassert x == 99.5\n");
            AssertClean(s, o);
            Assert.AreEqual(F64.FromInt(99) + F64.Half, Num(s, "x"));
        }

        [Test]
        public void Literal_Quarter_IsExact()
        {
            var (s, o) = RunMath("x = 0.25\nassert x * 4 == 1\n");
            AssertClean(s, o);
            Assert.AreEqual(F64.FromRaw(1L << 30), Num(s, "x")); // 0.25 == 2^30 in 32.32
        }

        [Test]
        public void Literal_PointSeven_IsTruncatedDeterministically()
        {
            // 0.7 is NOT exactly representable. The parser truncates deterministically:
            // (7 << 32) / 10. The script value must equal that exact raw, and be
            // strictly less than 0.7-rounded-up — i.e. the truncation is downward.
            var (s, o) = RunMath("x = 0.7\n");
            AssertClean(s, o);
            Assert.AreEqual(F64.FromRaw((7L << 32) / 10), Num(s, "x"));
        }

        [Test]
        public void Literal_NegativeFraction_Exact()
        {
            var (s, o) = RunMath("x = -2.5\nassert x == -2.5\nassert x + 2.5 == 0\n");
            AssertClean(s, o);
            Assert.AreEqual(-(F64.Two + F64.Half), Num(s, "x"));
        }

        [Test]
        public void Literal_ManyFractionalDigits_TruncatedAtNine()
        {
            // Digits past the 9th are dropped; 0.1234567891 and 0.123456789 share a raw.
            var (s, o) = RunMath("a = 0.123456789\nb = 0.1234567891\nassert a == b\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  F64 → int truncation (toward zero) — indices, counts, ToInt
        // ──────────────────────────────────────────────────────────

        [Test]
        public void ArrayIndex_FractionalTruncatesTowardZero()
        {
            // index 2.9 must truncate to 2 (toward zero), not round to 3 or floor.
            var (s, o) = RunMath("arr = {10, 20, 30}\nassert arr{2.9} == 30\nassert arr{0.99} == 10\n");
            AssertClean(s, o);
        }

        [Test]
        public void StringRepeat_FractionalCountTruncates()
        {
            // "ab" * 3.9 → 3 repeats (count truncates toward zero).
            var (s, o) = RunMath("s = \"ab\" * 3.9\nassert s == \"ababab\"\n");
            AssertClean(s, o);
        }

        [Test]
        public void RangeBound_FractionalTruncates()
        {
            // loop upper bound 3.9 → iterates 0,1,2 (exclusive, truncated).
            var (s, o) = RunMath("c = 0\nloop i in 0..3.9\n  c = c + 1\nend\nassert c == 3\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  round (half-up), floor, ceil — incl. negatives
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Round_HalfUp_Positive()
        {
            // Fixed64.Round is half-up: 0.5→1, 2.5→3 (NOT banker's, which gives 2).
            var (s, o) = RunMath("assert round[0.5] == 1\nassert round[2.5] == 3\nassert round[1.4] == 1\n");
            AssertClean(s, o);
        }

        [Test]
        public void Round_HalfUp_Negative()
        {
            // half-up means -0.5 → 0 (toward +inf at the .5 tie), -1.5 → -1.
            var (s, o) = RunMath("assert round[0 - 0.5] == 0\nassert round[0 - 1.5] == 0 - 1\n");
            AssertClean(s, o);
        }

        [Test]
        public void Floor_And_Ceil_Negatives()
        {
            var (s, o) = RunMath(
                "assert floor[2.7] == 2\n" +
                "assert ceil[2.1] == 3\n" +
                "assert floor[0 - 2.1] == 0 - 3\n" +   // floor toward -inf
                "assert ceil[0 - 2.9] == 0 - 2\n");    // ceil toward +inf
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  Divide / modulo by zero — catchable (D4), not a crash
        // ──────────────────────────────────────────────────────────

        [Test]
        public void DivideByZero_Raises_AndIsCatchable()
        {
            var (s, o) = RunMath(
                "caught = false\n" +
                "begin\n" +
                "  x = 1 / 0\n" +
                "rescue err\n" +
                "  caught = true\n" +
                "end\n" +
                "assert caught == true\n");
            AssertClean(s, o);
            Assert.IsTrue(Num(s, "caught") == F64.One ||
                          s.UserMemoryScope.Get("caught").LogicalValue);
        }

        [Test]
        public void ModuloByZero_Raises_AndIsCatchable()
        {
            var (s, o) = RunMath(
                "caught = false\n" +
                "begin\n" +
                "  x = 5 % 0\n" +
                "rescue err\n" +
                "  caught = true\n" +
                "end\n" +
                "assert caught == true\n");
            AssertClean(s, o);
        }

        [Test]
        public void DivideByZero_Uncaught_LogsAndDoesNotCrash()
        {
            // At top level (no rescue) a divide-by-zero surfaces as a logged message
            // rather than crashing the host; the VM keeps running. (Inside begin/rescue
            // it is catchable — see DivideByZero_Raises_AndIsCatchable.)
            var (s, o) = RunMath("x = 1 / 0\n");
            Assert.IsTrue(o.Exists(line => line.Contains("Division by zero")),
                $"Expected a 'Division by zero' diagnostic. Output:\n{string.Join("\n", o)}");
        }

        [Test]
        public void FractionalDivideByZero_IsCatchable()
        {
            // 1.5 / 0.0 is a divide-by-zero just like the integer form, and is
            // catchable with begin/rescue.
            var (s, o) = RunMath(
                "caught = false\n" +
                "begin\n" +
                "  x = 1.5 / 0.0\n" +
                "rescue err\n" +
                "  caught = true\n" +
                "end\n" +
                "assert caught == true\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  Exact fixed-point arithmetic identities
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Arithmetic_ExactWhereRepresentable()
        {
            var (s, o) = RunMath(
                "assert 0.5 + 0.25 == 0.75\n" +
                "assert 1.5 * 2 == 3\n" +
                "assert 10 / 4 == 2.5\n" +       // 10/4 = 2.5 exactly representable
                "assert 7 % 3 == 1\n" +
                "assert 0.75 - 0.25 == 0.5\n");
            AssertClean(s, o);
        }

        [Test]
        public void Modulo_FractionalOperands()
        {
            // 5.5 % 2 == 1.5 exactly (all representable).
            var (s, o) = RunMath("assert 5.5 % 2 == 1.5\n");
            AssertClean(s, o);
        }

        [Test]
        public void Negative_Arithmetic_SignHandling()
        {
            var (s, o) = RunMath(
                "assert (0 - 6) / 2 == 0 - 3\n" +
                "assert (0 - 7) % 3 == 0 - 1\n" +   // C#-style truncated modulo sign
                "assert 0 - (0 - 5) == 5\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  Transcendentals — approximate, asserted with tolerance
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Sqrt_ApproximatesWithinTolerance()
        {
            // sqrt is a fixed-point approximation; assert within a tolerance band
            // rather than exact equality. abs(sqrt(2)^2 - 2) must be small.
            var (s, o) = RunMath(
                "r = sqrt[2.0]\n" +
                "d = r * r - 2\n" +
                "assert abs[d] < 0.001\n");
            AssertClean(s, o);
        }

        [Test]
        public void Sqrt_PerfectSquare_NearExact()
        {
            // sqrt(4) ≈ 2 but not necessarily bit-exact — tolerance assert.
            var (s, o) = RunMath("r = sqrt[4.0]\nassert abs[r - 2] < 0.001\n");
            AssertClean(s, o);
        }

        [Test]
        public void Pow_ApproximatesWithinTolerance()
        {
            // pow(2,10) ≈ 1024 but approximate; band assert.
            var (s, o) = RunMath("p = pow[2.0, 10.0]\nassert abs[p - 1024] < 0.5\n");
            AssertClean(s, o);
        }

        [Test]
        public void Trig_SinCosIdentity_WithinTolerance()
        {
            // sin^2 + cos^2 ≈ 1 for an arbitrary angle.
            var (s, o) = RunMath(
                "a = 0.7\n" +
                "v = sin[a] * sin[a] + cos[a] * cos[a]\n" +
                "assert abs[v - 1] < 0.001\n");
            AssertClean(s, o);
        }

        [Test]
        public void Pi_IsApproximatelyCorrect()
        {
            var (s, o) = RunMath("p = pi[]\nassert p > 3.14\nassert p < 3.15\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  clamp / min / max / abs / sign — exact integer-ish ops
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Clamp_Min_Max_Abs_Sign_Exact()
        {
            var (s, o) = RunMath(
                "assert clamp[5, 0, 3] == 3\n" +
                "assert clamp[0 - 5, 0, 3] == 0\n" +
                "assert clamp[1.5, 0, 3] == 1.5\n" +
                "assert min[2, 5] == 2\n" +
                "assert max[2, 5] == 5\n" +
                "assert abs[0 - 4.5] == 4.5\n" +
                "assert sign[0 - 3] == 0 - 1\n" +
                "assert sign[3] == 1\n" +
                "assert sign[0] == 0\n");
            AssertClean(s, o);
        }

        [Test]
        public void Lerp_Endpoints_AndMidpoint()
        {
            var (s, o) = RunMath(
                "assert lerp[0, 10, 0] == 0\n" +
                "assert lerp[0, 10, 1] == 10\n" +
                "assert lerp[0, 10, 0.5] == 5\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  Representable-range boundary (integer part ±2147483647)
        // ──────────────────────────────────────────────────────────

        [Test]
        public void LargeIntegerLiteral_AtBoundary_Parses()
        {
            // 2147483647 is the documented max integer part; must parse and compare.
            var (s, o) = RunMath("x = 2147483647\nassert x == 2147483647\n");
            AssertClean(s, o);
        }

        [Test]
        public void NumericComparison_AcrossFractionalBoundary()
        {
            var (s, o) = RunMath(
                "assert 0.5 < 0.50001\n" +
                "assert 0.9999 < 1\n" +
                "assert 1.0001 > 1\n");
            AssertClean(s, o);
        }
    }
}
