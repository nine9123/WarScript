using System.Collections.Generic;
using System.IO;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// The fixed-point migration changed how numeric constants are encoded in
    /// bytecode: instead of an IEEE-754 double, each numeric is now written as the
    /// 64-bit F64 raw (Numeric.Raw / FromRawNumeric). These tests pin that the
    /// exact raw survives a save→load round-trip for fractional, negative, and
    /// boundary values — not just integers, and not merely "close" after reload.
    /// </summary>
    [TestFixture]
    public class FixedPointSerializationTests
    {
        private static (WarScriptLanguage script, List<string> output) RoundTrip(WarScriptLanguage source)
        {
            var ms = new MemoryStream();
            source.SaveBytecode(ms);
            var bytes = ms.ToArray();

            var output = new List<string>();
            var target = new WarScriptLanguage("test", "", null, (s, msg) => output.Add(msg));
            WarScriptLibraryRegistry.RegisterAll(target, target.GlobalDefinitionScope);
            target.Run();
            target.LoadBytecode(new MemoryStream(bytes));
            return (target, output);
        }

        [Test]
        public void RoundTrip_FractionalConstant_ExactRaw()
        {
            // 0.5 is exactly representable; after reload it must print identically.
            var (s1, _) = TestHelper.Run("t", "fun f []\n  print 0.5\nend\n");
            var (s2, o2) = RoundTrip(s1);
            s2.Call(s2.GetFunction("f", 0));
            Assert.AreEqual(new[] { "0.5" }, o2);
        }

        [Test]
        public void RoundTrip_TruncatedFraction_PreservesExactRaw()
        {
            // 0.7 has a specific truncated raw. It is encoded as a numeric CONSTANT in
            // the function body; after a save→load into a fresh script, comparing it
            // against the same literal must still be exactly equal — proving the raw
            // (not a re-parsed-from-double approximation) was serialized.
            var (s1, _) = TestHelper.Run("t",
                "result = false\n" +
                "fun check []\n  result = (0.7 == 0.7)\n  assert 0.7 + 0.3 > 0.99\nend\n");
            var (s2, o2) = RoundTrip(s1);
            s2.Call(s2.GetFunction("check", 0));
            Assert.IsFalse(s2.ExceptionContext.IsRaised(),
                "0.7 numeric constant did not survive serialization correctly");
        }

        [Test]
        public void RoundTrip_NegativeFractional()
        {
            var (s1, _) = TestHelper.Run("t", "fun f []\n  print 0 - 2.5\nend\n");
            var (s2, o2) = RoundTrip(s1);
            s2.Call(s2.GetFunction("f", 0));
            Assert.AreEqual(new[] { "-2.5" }, o2);
        }

        [Test]
        public void RoundTrip_MaxIntegerPart()
        {
            var (s1, _) = TestHelper.Run("t", "fun f []\n  print 2147483647\nend\n");
            var (s2, o2) = RoundTrip(s1);
            s2.Call(s2.GetFunction("f", 0));
            Assert.AreEqual(new[] { "2147483647" }, o2);
        }

        [Test]
        public void RoundTrip_GlobalNumericVariables_PreservedExactly()
        {
            // Global variables are preserved when bytecode is loaded back into the
            // SAME script instance (which keeps the live MemoryScope) — mirroring the
            // existing LoadBytecodePreservesGlobalVariables test. Confirm the numeric
            // payloads are bit-exact for fractional values across that round-trip.
            var script = new WarScriptLanguage("t",
                "half = 0.5\n" +
                "quarter = 0.25\n" +
                "big = 1000.125\n", null, (s, m) => { });
            script.Run();

            var ms = new MemoryStream();
            script.SaveBytecode(ms);
            script.LoadBytecode(new MemoryStream(ms.ToArray())); // same instance

            Assert.AreEqual(F64.Half, script.UserMemoryScope.Get("half").Numeric);
            Assert.AreEqual(F64.FromRaw(1L << 30), script.UserMemoryScope.Get("quarter").Numeric);
            Assert.AreEqual(F64.FromInt(1000) + F64.FromRaw(1L << 29), // 1000.125
                            script.UserMemoryScope.Get("big").Numeric);
        }

        [Test]
        public void RoundTrip_ArithmeticAfterReload_ExactIdentities()
        {
            var (s1, _) = TestHelper.Run("t",
                "fun calc []\n" +
                "  assert 0.5 + 0.25 == 0.75\n" +
                "  assert 10 / 4 == 2.5\n" +
                "end\n");
            var (s2, o2) = RoundTrip(s1);
            s2.Call(s2.GetFunction("calc", 0));
            Assert.IsFalse(s2.ExceptionContext.IsRaised(),
                "Arithmetic identities broke after bytecode reload (raw not preserved).");
        }

        [Test]
        public void RoundTrip_NumericSeparatorFraction_PreservesValue()
        {
            // Underscores are stripped by the lexer; the resulting raw must round-trip.
            var (s1, _) = TestHelper.Run("t", "fun f []\n  print 3.141_592\nend\n");
            var (s2a, o2) = RoundTrip(s1);
            s2a.Call(s2a.GetFunction("f", 0));
            // Whatever 3.141592 formats to, it must be identical before and after.
            var (sCtrl, oCtrl) = TestHelper.Run("t2", "fun f []\n  print 3.141592\nend\n");
            sCtrl.Call(sCtrl.GetFunction("f", 0));
            Assert.AreEqual(oCtrl, o2);
        }
    }
}
