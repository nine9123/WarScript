using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;
using WarScript.Parser;

namespace Tests
{
    /// <summary>
    /// Boundary, overflow, and timing-drift edge cases for the fixed-point migration
    /// that the rest of the suite does not touch. These PIN deterministic-but-surprising
    /// behavior so a future regression (e.g. someone swapping in saturating math, or
    /// changing the coroutine carry semantics) is caught:
    ///   - 32.32 arithmetic wraps (two's complement); it does NOT saturate or throw
    ///   - abs(MinValue) / -MinValue stay negative (FixPointCS's documented LONG_MIN trap)
    ///   - coroutine `wait` accumulates dt as a carried remainder — drift with a
    ///     non-representable step (0.1) is bounded and deterministic
    ///   - a script literal and a host F64.FromDouble produce the same raw
    ///   - NativeObject handles (the F64Vec3 model, D5) pass through scripts opaquely
    /// </summary>
    [TestFixture]
    public class FixedPointBoundaryTests
    {
        private static (WarScriptLanguage script, List<string> output) RunMath(string source)
        {
            return TestHelper.Run("fp_bound.ws", source,
                (s, scope) => MathLibrary.Register(s, scope));
        }

        private static void AssertClean(WarScriptLanguage script, List<string> output)
        {
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script raised unexpectedly. Output:\n{string.Join("\n", output)}");
        }

        private static F64 Num(WarScriptLanguage s, string var) => s.UserMemoryScope.Get(var).Numeric;

        // ──────────────────────────────────────────────────────────
        //  Overflow: 32.32 arithmetic WRAPS (two's complement), no saturate/throw
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Addition_OverflowWrapsToNegative()
        {
            // MaxValue + One wraps past the top of the signed 64-bit raw → negative.
            // This is deterministic; pin it so a switch to saturating math is caught.
            Assert.IsTrue(F64.MaxValue + F64.One < F64.Zero,
                "MaxValue + 1 should wrap to a negative raw (two's complement).");
        }

        [Test]
        public void Subtraction_UnderflowWrapsToPositive()
        {
            Assert.IsTrue(F64.MinValue - F64.One > F64.Zero,
                "MinValue - 1 should wrap to a positive raw (two's complement).");
        }

        [Test]
        public void Abs_OfMinValue_StaysNegative_DocumentedTrap()
        {
            // FixPointCS Fixed64.Abs is documented to fail for LONG_MIN: abs(MinValue)
            // returns MinValue (still negative). Pinning this so it's a known quantity,
            // not a surprise discovered during a desync hunt.
            Assert.IsTrue(F64.Abs(F64.MinValue) < F64.Zero,
                "abs(MinValue) is the LONG_MIN trap and stays negative.");
            Assert.AreEqual(F64.MinValue, F64.Abs(F64.MinValue));
        }

        [Test]
        public void Negate_OfMinValue_StaysNegative()
        {
            // -MinValue has no positive representation in two's complement; it wraps
            // back to MinValue.
            Assert.AreEqual(F64.MinValue, -F64.MinValue);
        }

        [Test]
        public void Multiply_LargeProduct_ExceedsIntRange_IsDeterministic()
        {
            // 100000 * 100000 = 1e10, beyond the ±2147483647 integer-part range.
            // The result is whatever the fixed-point multiply deterministically yields;
            // assert it's stable and equal to a second identical computation (no UB).
            var a = F64.FromInt(100000);
            var first = a * a;
            var second = F64.FromInt(100000) * F64.FromInt(100000);
            Assert.AreEqual(first, second, "Large fixed-point product must be deterministic.");
        }

        [Test]
        public void NegativeZero_EqualsZero()
        {
            // Fixed-point is integer-backed: there is no distinct -0.
            Assert.AreEqual(F64.Zero, F64.FromRaw(0));
            Assert.IsTrue(F64.Zero == -F64.Zero);
        }

        [Test]
        public void Script_LargeArithmetic_DoesNotCrashHost()
        {
            // Exercise overflow through the VM: a big multiply must run to completion
            // (wrap is fine) rather than throwing out of the interpreter.
            var (s, o) = RunMath("x = 100000\ny = x * x * x\nassert y == y\n");
            AssertClean(s, o);
        }

        // ──────────────────────────────────────────────────────────
        //  Literal ↔ host value agreement
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Literal_MatchesHostFromDouble_ForSameValue()
        {
            // A script literal 0.1 must produce the SAME raw as the host computing
            // F64.FromDouble(0.1); otherwise script-authored and engine-fed values
            // would silently disagree.
            Assert.AreEqual(F64.FromDouble(0.1).Raw, NumericLiteral.Parse("0.1").Raw);
            Assert.AreEqual(F64.FromDouble(0.5).Raw, NumericLiteral.Parse("0.5").Raw);
            Assert.AreEqual(F64.FromDouble(3.14159).Raw, NumericLiteral.Parse("3.14159").Raw);
        }

        [Test]
        public void Script_ReadsHostFedFractionalValue_Exactly()
        {
            // A value injected from the host as a call argument is observed in-script
            // with its exact raw (no re-quantization on the boundary).
            var (script, output) = TestHelper.Run("t",
                "received = 0\nfun take [v]\n  received = v\nend\n",
                (s, scope) => MathLibrary.Register(s, scope));
            script.Call(script.GetFunction("take", 1), WarValue.FromNumeric(F64.FromDouble(0.1)));
            Assert.AreEqual(F64.FromDouble(0.1), script.UserMemoryScope.Get("received").Numeric);
        }

        // ──────────────────────────────────────────────────────────
        //  Coroutine dt-accumulation drift (non-representable step)
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Coroutine_Wait_AccumulatesNonRepresentableStep_Deterministically()
        {
            // `wait 1.0` fed 0.1 steps. Because 0.1 isn't exactly representable and the
            // wait carries a running remainder (_waitRemaining -= dt, fires at <= 0),
            // the exact tick it fires on is a fixed, deterministic fact — pin it.
            var (script, output) = TestHelper.Run("t", @"
                fired = 0
                fun waiter []
                    yield wait 1.0
                    fired = 1
                end
            ");
            script.StartCoroutine("waiter", System.Array.Empty<WarValue>());

            var step = F64.FromDouble(0.1);
            int firedOnTick = -1;
            for (int t = 1; t <= 14; t++)
            {
                script.TickCoroutines(step);
                if (script.UserMemoryScope.Get("fired").Numeric == F64.One)
                {
                    firedOnTick = t;
                    break;
                }
            }

            // sum(0.1 x10) is just UNDER 1.0 (raw 4294967290 vs 4294967296), leaving a
            // tiny positive remainder after the 10th tick, so the timer fires on the 11th.
            Assert.AreEqual(11, firedOnTick,
                "wait 1.0 under 0.1 steps fires on tick 11 due to deterministic fixed-point drift.");
        }

        [Test]
        public void Coroutine_Wait_RepresentableStep_FiresExactlyOnBoundary()
        {
            // With an exactly-representable step (0.5) summing to 1.0 with no drift,
            // `wait 1.0` fires precisely on the 2nd tick.
            var (script, output) = TestHelper.Run("t", @"
                fired = 0
                fun waiter []
                    yield wait 1.0
                    fired = 1
                end
            ");
            script.StartCoroutine("waiter", System.Array.Empty<WarValue>());

            var half = F64.Half;
            script.TickCoroutines(half);
            Assert.AreEqual(F64.Zero, script.UserMemoryScope.Get("fired").Numeric,
                "Should not fire before the boundary.");
            script.TickCoroutines(half);
            Assert.AreEqual(F64.One, script.UserMemoryScope.Get("fired").Numeric,
                "0.5 + 0.5 == 1.0 exactly, so it fires on the 2nd tick.");
        }

        [Test]
        public void Coroutine_Wait_SingleLargeStep_FiresImmediately()
        {
            // A dt larger than the wait fires on the first tick (remainder goes <= 0).
            var (script, output) = TestHelper.Run("t", @"
                fired = 0
                fun waiter []
                    yield wait 0.5
                    fired = 1
                end
            ");
            script.StartCoroutine("waiter", System.Array.Empty<WarValue>());

            script.TickCoroutines(F64.Two); // 2.0 >> 0.5
            Assert.AreEqual(F64.One, script.UserMemoryScope.Get("fired").Numeric);
        }

        // ──────────────────────────────────────────────────────────
        //  NativeObject opaque passthrough (the F64Vec3 model — D5)
        // ──────────────────────────────────────────────────────────

        [Test]
        public void NativeObject_PassesThroughScriptVariable_Unchanged()
        {
            // A native handle assigned to a script variable and read back must be the
            // exact same reference — scripts treat it as an opaque box (D5: F64Vec3).
            var handle = new object();
            var (script, output) = TestHelper.Run("t",
                "stored = null\nfun keep [h]\n  stored = h\nend\n");
            script.Call(script.GetFunction("keep", 1), WarValue.FromNativeObject(handle));

            var stored = script.UserMemoryScope.Get("stored");
            Assert.IsTrue(stored.IsNativeObject);
            Assert.AreSame(handle, stored.Ref);
        }

        [Test]
        public void NativeObject_SurvivesArrayStorage()
        {
            // Round-trip a native handle through a WarScript array.
            var handle = new object();
            var (script, output) = TestHelper.Run("t",
                "box = {}\nfun put [h]\n  box << h\nend\nfun get []\n  return box{0}\nend\n",
                (s, scope) => ArrayLibrary.Register(s, scope));
            script.Call(script.GetFunction("put", 1), WarValue.FromNativeObject(handle));

            var arr = script.UserMemoryScope.Get("box");
            Assert.IsTrue(arr.IsArray);
            Assert.AreEqual(1, arr.ArrayValue.Count);
            Assert.IsTrue(arr.ArrayValue[0].IsNativeObject);
            Assert.AreSame(handle, arr.ArrayValue[0].Ref);
        }

        [Test]
        public void NativeObject_NotConfusedWithNumericOrNull()
        {
            var handle = new object();
            var v = WarValue.FromNativeObject(handle);
            Assert.IsTrue(v.IsNativeObject);
            Assert.IsFalse(v.IsNumeric);
            Assert.IsFalse(v.IsNull);
            Assert.IsFalse(v.IsText);
            Assert.IsFalse(v.IsArray);
        }
    }
}
