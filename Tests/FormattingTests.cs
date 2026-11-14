using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Parser;

namespace Tests
{
    /// <summary>
    /// Canonical formatting (plan 5.10, T1.6; DET-1, DET-2, DET-3): numbers
    /// through integer arithmetic only and the same in every culture, native
    /// objects without the host's <c>ToString()</c>. The formatting table and
    /// the <c>F64Vec3</c> repro are A.9 in <see cref="ReproTests"/>; A.8 is a golden.
    /// </summary>
    [TestFixture]
    public class FormattingTests
    {
        // ── Numbers ─────────────────────────────────────────────────────

        /// <summary>Raw values the formatter must handle, besides a pseudo-random sweep.</summary>
        private static readonly long[] EdgeRaws =
        {
            0, 1, -1, 2, -2, 1L << 31, -(1L << 31), 1L << 32, -(1L << 32),
            (1L << 32) - 1, -((1L << 32) - 1), long.MaxValue, long.MinValue, long.MinValue + 1,
            long.MaxValue - 1, 13486197309L, 3006477107L, 1431655765L, -1431655765L,
            (2147483647L << 32) + (1L << 31), -((2147483647L << 32) + (1L << 31)),
        };

        private static IEnumerable<long> Raws()
        {
            foreach (var raw in EdgeRaws) yield return raw;
            ulong x = 0x9E3779B97F4A7C15UL; // xorshift, fixed seed: deterministic
            for (int i = 0; i < 20000; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                yield return unchecked((long)x);
                yield return unchecked((long)x) >> (int)(x % 40); // smaller magnitudes too
            }
        }

        [Test]
        public void EveryFormattedNumber_DenotesItsRawValue()
        {
            foreach (var raw in Raws())
            {
                var text = WarValue.FromRawNumeric(raw).ToString();
                var failure = CheckFormatted(raw, text);
                if (failure != null) Assert.Fail($"raw {raw}: '{text}': {failure}");
            }
        }

        /// <summary>
        /// Null when <paramref name="text"/> is a canonical form of <paramref name="raw"/>:
        /// ASCII only; up to 9 fractional digits that parse back to the raw value,
        /// or else the exact value of the raw (an exact decimal with no trailing zero);
        /// an integer is its exact value.
        /// </summary>
        private static string CheckFormatted(long raw, string text)
        {
            if (!Regex.IsMatch(text, @"^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$"))
                return "not of the form -?digits(.digits)";
            int dot = text.IndexOf('.');
            int fracDigits = dot < 0 ? 0 : text.Length - dot - 1;
            // An integer is checked exactly: the parser cannot read -2147483648 (F64.MinValue).
            if (dot >= 0 && fracDigits <= 9)
            {
                if (!NumericLiteral.TryParseRaw(text, out var parsed)) return "does not parse";
                return parsed == raw ? null : $"parses to raw {parsed}";
            }
            // An integer, or the exact expansion: text * 2^32 must equal raw.
            var digits = BigInteger.Parse(text.Replace(".", "").Replace("-", ""), CultureInfo.InvariantCulture);
            if (text[0] == '-') digits = -digits;
            var scaled = digits * (BigInteger.One << 32);
            var expected = new BigInteger(raw) * BigInteger.Pow(10, fracDigits);
            if (scaled != expected) return "is not the exact value";
            return fracDigits > 32 ? "has more than 32 fractional digits" : null;
        }

        [Test]
        public void Formatting_IsTheSameInEveryCulture()
        {
            var expected = new List<string>();
            foreach (var raw in Raws()) expected.Add(WarValue.FromRawNumeric(raw).ToString());

            var saved = CultureInfo.CurrentCulture;
            try
            {
                foreach (var name in Golden.GoldenRunner.Cultures)
                {
                    CultureInfo.CurrentCulture = name.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(name);
                    int i = 0;
                    foreach (var raw in Raws())
                        Assert.AreEqual(expected[i++], WarValue.FromRawNumeric(raw).ToString(), $"raw {raw} in '{name}'");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void ShortCandidate_IsPreferredOverTheExactExpansion()
        {
            Assert.AreEqual("0.1", WarValue.FromNumeric(NumericLiteral.Parse("0.1")).ToString());
            Assert.AreEqual("-12.125", WarValue.FromNumeric(NumericLiteral.Parse("-12.125")).ToString());
            Assert.AreEqual("0.000000001", WarValue.FromNumeric(NumericLiteral.Parse("0.000000001")).ToString());
            // F64.Ratio(1, 3) has no short form (plan A.9).
            Assert.AreEqual("0.33333333325572311878204345703125", WarValue.FromNumeric(F64.Ratio(1, 3)).ToString());
            Assert.AreEqual("-0.33333333325572311878204345703125", WarValue.FromNumeric(-F64.Ratio(1, 3)).ToString());
        }

        [Test]
        public void TryParseRaw_AgreesWithParse()
        {
            foreach (var text in new[] { "0", "-0", "1.5", "-1.5", "2147483647", "-2147483647.999999999", "007", "1.0000000001" })
            {
                Assert.IsTrue(NumericLiteral.TryParseRaw(text, out var raw), text);
                Assert.AreEqual(NumericLiteral.Parse(text).Raw, raw, text);
            }
            foreach (var text in new[] { "", "-", ".", "1.2.3", "1e5", "2147483648", "-2147483648.5", "1,5" })
            {
                Assert.IsFalse(NumericLiteral.TryParseRaw(text, out _), text);
                Assert.Throws<WarScript.Exception.SyntaxException>(() => NumericLiteral.Parse(text), text);
            }
        }

        [Test]
        public void ScriptOutput_UsesTheCanonicalNumberFormat()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                var (_, output) = TestHelper.Run("t", @"
x = 0 - 1500.25
print x
print ""v="" + x
print {x, 0 - 7}
print ""{x} and {1 / 3}""");
                CollectionAssert.AreEqual(new[]
                {
                    "-1500.25", "v=-1500.25", "[-1500.25, -7]",
                    "-1500.25 and 0.33333333325572311878204345703125",
                }, output);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        // ── Native objects (PROP-14) ────────────────────────────────────

        private sealed class HostThing
        {
            public int Id;
            public override string ToString() => throw new InvalidOperationException("host ToString called");
        }

        private sealed class OtherThing
        {
        }

        private static (WarScriptLanguage, List<string>) RunWithThings(string source, Action<WarScriptLanguage> configure = null)
        {
            return TestHelper.Run("t", source, (s, scope) =>
            {
                scope.AddFunction(new NativeFunctionDefinition(
                    new FunctionDetails("thing", new List<string>()),
                    _ => WarValue.FromNativeObject(new HostThing { Id = 7 }), "", ""));
                scope.AddFunction(new NativeFunctionDefinition(
                    new FunctionDetails("other", new List<string>()),
                    _ => WarValue.FromNativeObject(new OtherThing()), "", ""));
                scope.AddFunction(new NativeFunctionDefinition(
                    new FunctionDetails("vec", new List<string>()),
                    _ => WarValue.FromNativeObject(new F64Vec3(F64.FromInt(-3), F64.Ratio(1, 2), F64.Ratio(1, 3))), "", ""));
                configure?.Invoke(s);
            });
        }

        [Test]
        public void NativeObject_WithoutAFormatter_PrintsItsTypeName()
        {
            var (_, output) = RunWithThings(@"
t = thing []
print t
print ""t="" + t
print {t, other []}");
            CollectionAssert.AreEqual(new[] { "<HostThing>", "t=<HostThing>", "[<HostThing>, <OtherThing>]" }, output);
        }

        [Test]
        public void RegisteredFormatter_IsUsedByPrintAndTextOperators()
        {
            var (_, output) = RunWithThings(@"
t = thing []
print t
print ""t="" + t
print ""in text: {t}""
print {t, other []}",
                s => s.RegisterNativeFormatter<HostThing>(h => "thing#" + h.Id.ToString(CultureInfo.InvariantCulture)));
            CollectionAssert.AreEqual(new[]
            {
                "thing#7", "t=thing#7", "in text: thing#7", "[thing#7, <OtherThing>]",
            }, output);
        }

        [Test]
        public void FormatterThrowingScriptError_RaisesARescuableError()
        {
            var (_, output) = RunWithThings(@"
t = thing []
begin
    print ""t="" + t
rescue e
    print ""rescued: "" + e
end",
                s => s.RegisterNativeFormatter<HostThing>(h =>
                    throw new WarScript.Exception.ScriptErrorException(
                        ScriptErrorKind.NativeError, "cannot format")));
            Assert.AreEqual(1, output.Count, string.Join(" | ", output));
            StringAssert.StartsWith("rescued: ", output[0]);
            StringAssert.Contains("cannot format", output[0]);
        }

        [Test]
        public void FormatterThrowingAnythingElse_IsAHostFault()
        {
            // A formatter is host code, not a native function (6.9): any other
            // exception it throws is a failure of the host and stops the entry.
            var (script, output) = RunWithThings(@"
t = thing []
begin
    print ""t="" + t
rescue e
    print ""rescued""
end
print ""not reached""",
                s => s.RegisterNativeFormatter<HostThing>(h => throw new InvalidOperationException("formatter failed")));
            Assert.IsInstanceOf<InvalidOperationException>(script.LastHostFault);
            Assert.AreEqual(1, output.Count, string.Join(" | ", output));
            StringAssert.StartsWith("[WarScript] t:0:0: HostFault: formatter failed\nSystem.InvalidOperationException: formatter failed", output[0]);
        }

        [Test]
        public void F64Vec3_UsesTheNumberFormat_EvenWithAFormatterRegistered()
        {
            var (_, output) = RunWithThings(@"
print vec []
print ""at "" + vec []",
                s => s.RegisterNativeFormatter<F64Vec3>(v => "custom"));
            const string expected = "(-3, 0.5, 0.33333333325572311878204345703125)";
            CollectionAssert.AreEqual(new[] { expected, "at " + expected }, output);
        }

        [Test]
        public void FunctionValue_PrintsAsFun()
        {
            var (_, output) = TestHelper.Run("t", @"
double = fun [x] return x * 2 end
print double
print ""f: "" + double");
            CollectionAssert.AreEqual(new[] { "<fun lambda>", "f: <fun lambda>" }, output);
        }

        // ── Source scan (T1.6) ──────────────────────────────────────────

        /// <summary>
        /// Every parameterless <c>ToString()</c> in <c>Runtime/</c>, as
        /// "<c>File.cs: receiver</c>", whose receiver is known not to be a
        /// number (a number's <c>ToString()</c> depends on the culture: DET-1).
        /// A new call fails the scan: give a number
        /// <c>CultureInfo.InvariantCulture</c>, or add a non-number here.
        /// </summary>
        private static readonly HashSet<string> NonNumericToString = new HashSet<string>
        {
            "AuditReport.cs: sb",                      // StringBuilder
            "AuditReport.cs: e.Kind",                  // AuditKind
            "AuditReport.cs: sb.Append('\"')",         // StringBuilder
            "Chunk.cs: sb",
            "WarVM.cs: v.Tag",                         // ValueTag
            "ExceptionContext.cs: Exception",          // WarScript.Exception.Exception
            "Exception.cs: builder",
            "AdditionOperator.cs: left",               // WarValue (tree-walker operators)
            "AdditionOperator.cs: right",
            "ArrayValueOperator.cs: value",
            "ClassPropertyOperator.cs: value",
            "EqualsOperator.cs: left",
            "EqualsOperator.cs: right",
            "MultiplicationOperator.cs: left",
            "MultiplicationOperator.cs: right",
            "NotEqualsOperator.cs: left",
            "NotEqualsOperator.cs: right",
            "SubtractionOperator.cs: left",
            "SubtractionOperator.cs: right",
            "WarValue.cs: sb",
            "WarValue.cs: other",                      // WarValue
            "LexicalParser.cs: c",                     // char
            "LexicalParser.cs: segment",               // StringBuilder
            "StateHash.cs: value",                     // WarValue
            "PrintStatement.cs: value",                // WarValue
            "ScriptResult.cs: raised.Value",           // WarValue
            "ScriptResult.cs: Kind",                   // ScriptErrorKind
            "ScriptResult.cs: Status",                 // ScriptStatus
            "ScriptResult.cs: Error",                  // ScriptError
            "ScriptResult.cs: text",                   // StringBuilder
        };

        [Test]
        public void Runtime_HasNoCultureDependentToString()
        {
            var problems = new List<string>();
            foreach (var path in Directory.GetFiles(RuntimeRoot(), "*.cs", SearchOption.AllDirectories))
            {
                var file = Path.GetFileName(path);
                var lines = File.ReadAllLines(path);
                for (int n = 0; n < lines.Length; n++)
                {
                    var line = lines[n];
                    if (line.TrimStart().StartsWith("//")) continue;
                    int at = 0;
                    while ((at = line.IndexOf(".ToString(", at, StringComparison.Ordinal)) >= 0)
                    {
                        var close = line.IndexOf(')', at + 10);
                        var args = close < 0 ? "" : line.Substring(at + 10, close - at - 10).Trim();
                        if (args.Length == 0)
                        {
                            var key = file + ": " + Receiver(line, at);
                            if (!NonNumericToString.Contains(key))
                                problems.Add($"{file}:{n + 1}: '{key}'");
                        }
                        else if (!args.Contains("Culture"))
                        {
                            problems.Add($"{file}:{n + 1}: ToString({args}) without a culture");
                        }
                        at += 10;
                    }
                }
            }
            Assert.IsEmpty(problems,
                "ToString() calls that may format a number in the current culture (T1.6, DET-1); " +
                "pass CultureInfo.InvariantCulture, or list a non-number in NonNumericToString:\n" +
                string.Join("\n", problems));
        }

        /// <summary>
        /// String operations whose string-argument overloads compare by the
        /// current culture (R1): <c>StartsWith</c>, <c>EndsWith</c>,
        /// <c>IndexOf</c> and <c>LastIndexOf</c> with a text argument,
        /// <c>string.Compare</c>, <c>ToUpper()</c>/<c>ToLower()</c>. Each call in
        /// <c>Runtime/</c> must name an ordinal or invariant comparison.
        /// </summary>
        [Test]
        public void Runtime_HasNoCultureSensitiveStringOperations()
        {
            var pattern = new Regex(
                @"\.(StartsWith|EndsWith|IndexOf|LastIndexOf)\(\s*""|[Ss]tring\.Compare\(|\.(ToUpper|ToLower)\(\s*\)");
            var problems = new List<string>();
            foreach (var path in Directory.GetFiles(RuntimeRoot(), "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(path);
                for (int n = 0; n < lines.Length; n++)
                {
                    var line = lines[n];
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (pattern.IsMatch(line) && !line.Contains("Ordinal") && !line.Contains("Invariant"))
                        problems.Add($"{Path.GetFileName(path)}:{n + 1}: {line.Trim()}");
                }
            }
            Assert.IsEmpty(problems,
                "culture-sensitive string operations in Runtime/ (R1); pass StringComparison.Ordinal:\n" +
                string.Join("\n", problems));
        }

        /// <summary>The expression a <c>.ToString(</c> at <paramref name="dot"/> is called on.</summary>
        private static string Receiver(string line, int dot)
        {
            int i = dot - 1, depth = 0;
            while (i >= 0)
            {
                char c = line[i];
                if (c == ')' || c == ']') depth++;
                else if (c == '(' || c == '[')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (depth == 0 && !(char.IsLetterOrDigit(c) || c == '_' || c == '.')) break;
                i--;
            }
            return line.Substring(i + 1, dot - i - 1);
        }

        private static string RuntimeRoot([CallerFilePath] string sourceFilePath = "")
        {
            // This file is Tests/FormattingTests.cs; the runtime is Runtime/.
            var root = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(sourceFilePath)), "Runtime");
            if (!Directory.Exists(root))
                Assert.Ignore("Runtime/ sources not found next to the tests: " + root);
            return root;
        }
    }
}
