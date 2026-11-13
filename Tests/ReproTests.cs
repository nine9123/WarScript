using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// The C# repros of the v2 plan, Appendix A (A.6, A.9, A.19, A.21): the
    /// ones that need a native function or a host value, so they cannot be
    /// golden files. Each check returns null when the v2 behaviour is observed
    /// and a description of the deviation otherwise; <see cref="KnownFailing"/>
    /// turns a deviation into Ignored until the named finding is fixed, and
    /// fails once the check passes, so the marker gets removed — the same
    /// contract as a golden's <c># known-failing:</c>.
    /// </summary>
    [TestFixture]
    public class ReproTests
    {
        // ── A.6 Re-entrant host call (CRASH-4, fixed by T1.5) ────────────

        [Test]
        public void A06_ReentrantHostCall()
        {
            var failure = Reentrant(@"
fun on_event [x]
    return x
end
fun main []
    a = 1
    b = fire []
    print ""after fire, a="" + a + "" b="" + b
    return 0
end
main []
print ""top done""", "after fire, a=1 b=7", "top done");
            Assert.IsNull(failure, failure);
        }

        [Test]
        public void A06_ReentrantCallbackErrorIsRescuedAtTheNativeCallSite()
        {
            // PROP-1: the callback's error continues from the native call site.
            var failure = Reentrant(@"
fun on_event [x]
    return x / 0
end
fun main []
    begin
        b = fire []
        print ""not reached""
    rescue e
        print ""rescued "" + e
    end
    return 0
end
main []
print ""top done""", "rescued Division by zero", "top done");
            Assert.IsNull(failure, failure);
        }

        [Test]
        public void A06_ReentrantCallbackErrorEndsTheOuterCall()
        {
            var failure = ReentrantError(@"
fun on_event [x]
    return x / 0
end
fun main []
    b = fire []
    print ""not reached""
    return 0
end
main []
print ""top done""", "Division by zero");
            Assert.IsNull(failure, failure);
        }

        [Test]
        public void A06_NativeAndScriptRecursionStopsAtMaxHostNesting()
        {
            // PROP-5: MaxHostNesting = 32 nested entries inside the outermost
            // one. The test caps the recursion itself at 64 levels so a
            // missing limit cannot overflow the C# stack of the test host.
            const int maxHostNesting = WarScriptLanguage.MaxHostNesting, testCap = 64;
            Assert.AreEqual(32, maxHostNesting);
            var depth = 0;
            var deepest = 0;
            WarScriptLanguage s = null;
            var output = new List<string>();
            s = new WarScriptLanguage("t", @"
fun on_event [x]
    return fire []
end
fire []
print ""not reached""", null, (w, m) => output.Add(m));
            s.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("fire", new List<string>()),
                args =>
                {
                    depth++;
                    deepest = Math.Max(deepest, depth);
                    try
                    {
                        if (depth < testCap)
                            s.Call(s.GetFunction("on_event", 1), WarValue.FromNumeric(42));
                    }
                    finally
                    {
                        depth--;
                    }
                    return WarValue.FromNumeric(7);
                }, "", ""));

            var failure = RunChecked(s, out var unhandled);
            if (failure == null && deepest > maxHostNesting + 1)
                failure = $"the native was entered {deepest} levels deep; MaxHostNesting is {maxHostNesting}";
            if (failure == null && unhandled == null)
                failure = "the run completed; expected LimitExceeded. Output: " + string.Join(" | ", output);
            if (failure == null && unhandled.Value.ToString() != "Host nesting limit exceeded")
                failure = "expected 'Host nesting limit exceeded', got: " + unhandled.Value;
            if (failure == null && deepest != maxHostNesting + 1)
                failure = $"the native was entered {deepest} levels deep, expected {maxHostNesting + 1} (one per entry)";
            if (failure == null && output.Contains("not reached"))
                failure = "the script continued after the nesting limit";
            Assert.IsNull(failure, failure);
        }

        /// <summary>
        /// Plan A.6: a native <c>fire</c> that calls <c>on_event[42]</c> on the
        /// same instance and returns 7. The printed output must equal
        /// <paramref name="expected"/>.
        /// </summary>
        private static string Reentrant(string source, params string[] expected) =>
            Reentrant(source, null, expected);

        /// <summary>As <see cref="Reentrant(string, string[])"/>, but the run must end
        /// with an unhandled error containing <paramref name="expectError"/>, printing nothing.</summary>
        private static string ReentrantError(string source, string expectError) =>
            Reentrant(source, expectError, new string[0]);

        private static string Reentrant(string source, string expectError, string[] expected)
        {
            WarScriptLanguage s = null;
            var output = new List<string>();
            s = new WarScriptLanguage("t", source, null, (w, m) => output.Add(m));
            s.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("fire", new List<string>()),
                args =>
                {
                    s.Call(s.GetFunction("on_event", 1), WarValue.FromNumeric(42));
                    // From Phase 2: assert the result is Ok and its value equals 42.
                    return WarValue.FromNumeric(7);
                }, "", ""));

            var failure = RunChecked(s, out var unhandled);
            if (failure != null)
                return failure;

            if (unhandled != null)
            {
                output.Remove(unhandled.ToString());
                if (expectError == null)
                    return "unhandled error: " + unhandled.Value;
                if (!unhandled.Value.ToString().Contains(expectError))
                    return $"expected an error containing '{expectError}', got: {unhandled.Value}";
            }
            else if (expectError != null)
            {
                return $"expected an unhandled error containing '{expectError}', but the run completed";
            }

            if (!output.SequenceEqual(expected))
                return $"printed [{string.Join(" | ", output)}], expected [{string.Join(" | ", expected)}]";
            return null;
        }

        // ── A.9 Formatting fractions and native objects (DET-2, DET-3, fixed by T1.6) ──

        [Test]
        public void A09_F64Vec3PrintsWithTheCanonicalNumberFormat()
        {
            var output = new List<string>();
            var s = new WarScriptLanguage("t", @"
fun show [v]
    print v
end", null, (w, m) => output.Add(m));
            var failure = RunChecked(s, out var unhandled);
            if (failure == null)
            {
                var vec = new F64Vec3(F64.FromInt(1), F64.Ratio(1, 3), F64.FromInt(-2));
                failure = InEveryCulture(() =>
                {
                    output.Clear();
                    s.Call(s.GetFunction("show", 1), WarValue.FromNativeObject(vec));
                    return Expect("(1, 0.33333333325572311878204345703125, -2)", output.LastOrDefault());
                });
            }
            Assert.IsNull(failure, failure);
        }

        /// <summary>Plan 5.10 and T1.6: the formatting table, identical in every culture.</summary>
        private static readonly (long raw, string text)[] FormattingTable =
        {
            (0L, "0"),
            (-1500L << 32, "-1500"),
            (1000L << 32, "1000"),
            (2147483648L, "0.5"),
            (-2147483648L, "-0.5"),
            (427349245952L, "99.5"),
            (13486197309L, "3.14"),         // the raw value of the literal 3.14
            (3006477107L, "0.7"),           // the raw value of the literal 0.7
            (1431655765L, "0.33333333325572311878204345703125"),   // F64.Ratio(1, 3)
            (long.MinValue, "-2147483648"),
            (long.MaxValue, "2147483647.99999999976716935634613037109375"),
            (1L, "0.00000000023283064365386962890625"),
        };

        [Test]
        public void A09_NumberFormattingTable()
        {
            var failure = InEveryCulture(() =>
            {
                foreach (var (raw, text) in FormattingTable)
                {
                    string actual;
                    try
                    {
                        actual = WarValue.FromRawNumeric(raw).ToString();
                    }
                    catch (System.Exception e)
                    {
                        return $"raw {raw}: {e.GetType().Name}: {e.Message}";
                    }
                    var mismatch = Expect(text, actual);
                    if (mismatch != null) return $"raw {raw}: {mismatch}";
                }
                return null;
            });
            Assert.IsNull(failure, failure);
        }

        // ── A.19 Disassembler (DX-5, fixed by T1.7) ─────────────────────

        [Test]
        public void A19_DisassembleHandlerOpcodes()
        {
            var s = new WarScriptLanguage("t", @"
fun f [arr, i]
    begin
        arr{i} = 1
    rescue e
        print e
    end
    return arr
end", null, (w, m) => { });
            Assert.IsNull(RunChecked(s, out _));
            var chunk = s.GetFunction("f", 2).Compiled.Chunk;
            string text = null;
            Assert.DoesNotThrow(() => text = chunk.Disassemble("f"));
            // One line per instruction, at the offsets the opcode table gives.
            // TODO(T1.7 verifier): also compare with the verifier's boundaries.
            var offsets = new List<int>();
            foreach (var line in text.Split('\n'))
                if (line.Length >= 4 && int.TryParse(line.Substring(0, 4), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var offset))
                    offsets.Add(offset);
            CollectionAssert.AreEqual(chunk.InstructionStarts(), offsets);
        }

        // ── A.21 A native method that throws (CRASH-3, fixed by T1.3) ────

        [Test]
        public void A21_NativeMethodExceptionIsRescued()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"
class Box [v]
end
install []
b = new Box [1]
begin
    b :: boom []
    print ""not reached""
rescue e
    print ""rescued "" + e
end
print ""done""", null, (w, m) => output.Add(m));

            // install[] runs while the script's definition scope is current and
            // gives the script's class Box a native method that throws.
            s.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("install", new List<string>()),
                args =>
                {
                    s.DefinitionContext.GetScope().GetClass("Box").GetDefinitionScope().AddFunction(
                        new NativeFunctionDefinition(
                            new FunctionDetails("boom", new List<string>()),
                            _ => throw new InvalidOperationException("boom failed"), "", ""));
                    return WarValue.Null;
                }, "", ""));

            var failure = RunChecked(s, out var unhandled);
            if (failure == null && unhandled != null)
                failure = "unhandled error: " + unhandled.Value;
            if (failure == null && (output.Count != 2 || !output[0].StartsWith("rescued ") ||
                                    !output[0].Contains("boom failed") || output[1] != "done"))
                failure = $"printed [{string.Join(" | ", output)}], expected [rescued <NativeError: boom failed> | done]";
            Assert.IsNull(failure, failure);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Runs the script, catching an escaped C# exception and checking the
        /// invariants (plan 9.3). Returns a failure description, or null;
        /// <paramref name="unhandled"/> is v1's logged unhandled error, if any.
        /// </summary>
        private static string RunChecked(WarScriptLanguage s, out WarScript.Exception.Exception unhandled)
        {
            unhandled = null;
            var before = Invariants.Capture(s);
            try
            {
                s.Run();
            }
            catch (System.Exception e)
            {
                return $"C# exception escaped: {e.GetType().Name}: {e.Message}";
            }
            unhandled = s.ExceptionContext.LastUnhandled;
            var violation = Invariants.Violation(s, before);
            return violation == null ? null : "invariant violated: " + violation;
        }

        private static string InEveryCulture(Func<string> check)
        {
            var savedCulture = CultureInfo.CurrentCulture;
            var savedUICulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var name in Golden.GoldenRunner.Cultures)
                {
                    var info = name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name);
                    CultureInfo.CurrentCulture = info;
                    CultureInfo.CurrentUICulture = info;
                    var failure = check();
                    if (failure != null)
                        return $"[{(name.Length == 0 ? "invariant" : name)}] {failure}";
                }
                return null;
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
                CultureInfo.CurrentUICulture = savedUICulture;
            }
        }

        private static string Expect(string expected, string actual) =>
            expected == actual ? null : $"expected '{expected}', got '{actual}'";

        private static void KnownFailing(string findingId, string failure)
        {
            if (failure != null)
                Assert.Ignore($"Known failing ({findingId}): {failure}");
            Assert.Fail($"Marked known-failing ({findingId}) but passes: remove the marker.");
        }
    }
}
