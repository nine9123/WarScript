using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Audit;

namespace Tests
{
    /// <summary>
    /// Audit mode (v2 plan T0.8, section 8.1): <see cref="WarScriptLanguage.AuditSink"/>
    /// receives an event wherever v1 takes a path whose behavior changes in
    /// v2, and <see cref="AuditReport"/> de-duplicates them by site.
    /// </summary>
    [TestFixture]
    public class AuditTests
    {
        private static List<AuditEvent> Audit(string source, System.Func<string, string> fileResolver = null)
        {
            var events = new List<AuditEvent>();
            TestHelper.Run("audit", source, (script, scope) => script.AuditSink = events.Add, fileResolver);
            return events;
        }

        private static string Format(IEnumerable<AuditEvent> events) =>
            string.Join("\n", events.Select(e => e.ToString()));

        [TestCase("a = 1 == \"1\"", AuditKind.MixedTypeEquality, 1)]
        [TestCase("a = 1 != \"2\"", AuditKind.MixedTypeEquality, 1)]
        [TestCase("a = 1 < \"2\"", AuditKind.MixedTypeOrdering, 1)]
        [TestCase("a = {1} >= {2}", AuditKind.MixedTypeOrdering, 1)]
        [TestCase("a = true < false", AuditKind.LogicalOrdering, 1)]
        [TestCase("a = \"ab\" - \"b\"", AuditKind.TextSubtraction, 1)]
        [TestCase("a = 1\nb = true + a", AuditKind.NonTextConcatenation, 2)]
        [TestCase("print true", AuditKind.LogicalFormatted, 1)]
        [TestCase("x = {1, {false}}\nprint x", AuditKind.LogicalFormatted, 2)]
        [TestCase("ok = true\nprint \"ok: {ok}\"", AuditKind.LogicalFormatted, 2)]
        [TestCase("a = -\"a\"", AuditKind.NegateNonNumeric, 1)]
        [TestCase("a = {1} + 2", AuditKind.ArrayPlusValue, 1)]
        [TestCase("j = 5\nj << 1", AuditKind.AppendToNonArray, 2)]
        [TestCase("arr = {1}\nk = arr{5}", AuditKind.IndexReadInvalid, 2)]
        [TestCase("arr = {1}\nk = arr{\"a\"}", AuditKind.IndexReadInvalid, 2)]
        [TestCase("five = 5\nm = five{0}", AuditKind.IndexReadInvalid, 2)]
        [TestCase("n = {1}\nn{9} = 1", AuditKind.IndexWriteInvalid, 2)]
        [TestCase("n = 4\nn{0} = 1", AuditKind.IndexWriteInvalid, 2)]
        [TestCase("t = \"abc\"\nt{0} = \"z\"", AuditKind.TextIndexAssign, 2)]
        [TestCase("fun f [t]\n  t{0} = \"z\"\nend\nf [\"abc\"]", AuditKind.TextIndexAssign, 2)]
        [TestCase("q = 3\nq :: p = 1", AuditKind.PropertyWriteOnNonInstance, 2)]
        [TestCase("class P [x]\nend\np = new P [1]\np :: z = 4", AuditKind.DynamicPropertyCreated, 4)]
        [TestCase("class P [x]\n  fun m []\n    this :: z = 1\n  end\nend\np = new P [1]\np :: m []", AuditKind.DynamicPropertyCreated, 3)]
        [TestCase("r = undefined_thing", AuditKind.UndefinedRead, 1)]
        [TestCase("g = 1\nfun f []\n  g = 2\nend\nf []", AuditKind.GlobalWriteFromFunction, 3)]
        [TestCase("g = 1\nh = fun [] g = 2 end\nh []", AuditKind.GlobalWriteFromFunction, 2)]
        [TestCase("class P [x]\n  fun m []\n    print x\n  end\nend\np = new P [1]\np :: m []", AuditKind.FieldAccessViaBareName, 3)]
        [TestCase("class P [x]\n  fun m []\n    x = 2\n  end\nend\np = new P [1]\np :: m []", AuditKind.FieldAccessViaBareName, 3)]
        [TestCase("g = 1\nclass P [x]\n  fun m []\n    print g\n  end\nend\np = new P [1]\np :: m []", AuditKind.GlobalShadowedInClass, 4)]
        [TestCase("g = 1\nclass P [x]\n  y = g\nend\np = new P [1]", AuditKind.GlobalShadowedInClass, 3)]
        [TestCase("if true\n  inner = 7\nend\nprint inner", AuditKind.BlockVariableEscapes, 4)]
        [TestCase("begin\n  inner = 7\n  raise \"x\"\nrescue e\nend\nprint inner", AuditKind.BlockVariableEscapes, 6)]
        [TestCase("class P [x]\nend\na = new P [1]\nb = new P [1]\nc = a == b", AuditKind.ClassStructuralEquality, 5)]
        public void EventIsReportedAtItsLine(string source, AuditKind kind, int line)
        {
            var events = Audit(source);
            var matching = events.Where(e => e.Kind == kind).ToList();
            Assert.AreEqual(1, matching.Count, "events:\n" + Format(events));
            Assert.AreEqual(line, matching[0].Line, "events:\n" + Format(events));
            Assert.AreEqual("audit", matching[0].Script);
        }

        [Test]
        public void V2CompatibleCodeReportsNothing()
        {
            var events = Audit(@"
                g = 1
                fun f [a]
                    b = a + 1
                    return b
                end
                class P [x]
                    y = 2
                    this :: z = 3
                    fun m []
                        this :: y = this :: x + 1
                        return this :: y
                    end
                end
                p = new P [1]
                q = p
                same = p == q
                n = p :: m []
                p :: x = 5
                arr = {1, 2}
                arr{1} = 3
                arr << 4
                total = 0
                loop v in arr
                    total += v
                end
                text = ""abc""
                first = text{0}
                less = ""a"" < ""b""
                eq = 1 == 1
                missing = null
                print ""{g} {f [2]} {n} {first}"" + total
                print -total
                if total > 0
                    total = f [total]
                end
                print total");
            Assert.AreEqual(0, events.Count, Format(events));
        }

        [Test]
        public void ReadingAVariableThatHoldsNullIsNotAnUndefinedRead()
        {
            var events = Audit("x = null\ny = x\narr = {null}\nz = arr{0}");
            Assert.AreEqual(0, events.Count, Format(events));
        }

        [Test]
        public void EventsNameTheImportTheyCameFrom()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun sub [a, b]\n  return a - b\nend\nx = 1 == \"1\""
            };
            var events = Audit("import \"lib\"\ny = sub [\"ab\", \"b\"]", path => files.TryGetValue(path, out var s) ? s : null);

            var equality = events.Single(e => e.Kind == AuditKind.MixedTypeEquality);
            Assert.AreEqual("lib", equality.Script);
            Assert.AreEqual(4, equality.Line);

            var subtraction = events.Single(e => e.Kind == AuditKind.TextSubtraction);
            Assert.AreEqual("lib", subtraction.Script, "a function defined in the import, called from the entry script");
            Assert.AreEqual(2, subtraction.Line);
            Assert.AreEqual("sub", subtraction.Function);
        }

        [Test]
        public void ReportGroupsEventsBySite()
        {
            var report = new AuditReport();
            TestHelper.Run("main", "loop i in 0..3\n  a = i == \"1\"\nend\nb = true < false",
                           (script, scope) => script.AuditSink = report.Add);

            Assert.AreEqual(4, report.EventCount);
            var entries = report.Entries;
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(AuditKind.MixedTypeEquality, entries[0].Kind);
            Assert.AreEqual(2, entries[0].Line);
            Assert.AreEqual(3, entries[0].Count);
            Assert.AreEqual("number 0 == text \"1\" (v1: false)", entries[0].FirstDetail);
            Assert.AreEqual(AuditKind.LogicalOrdering, entries[1].Kind);

            var markdown = report.ToMarkdown("Test");
            StringAssert.StartsWith("# Test\n\nEvents: 4, sites (kind, script, line): 2.\n", markdown);
            StringAssert.Contains("| `MixedTypeEquality` | 1 | 3 |", markdown);
            StringAssert.Contains("| `UndefinedRead` | 0 | 0 |", markdown);
            StringAssert.Contains("| main:2 | <script> | 3 | number 0 == text \"1\" (v1: false) |", markdown);

            var json = report.ToJson();
            StringAssert.StartsWith("{\"events\": 4, \"sites\": [\n", json);
            StringAssert.Contains("{\"kind\": \"MixedTypeEquality\", \"script\": \"main\", \"line\": 2, \"count\": 3, " +
                                  "\"functions\": [\"<script>\"], \"first\": \"number 0 == text \\\"1\\\" (v1: false)\"}", json);
        }

        [Test]
        public void AuditingDoesNotChangeTheRun()
        {
            const string source = "x = {1, true}\nprint x\nprint 1 == \"1\"\ny = \"ab\" - \"b\"\nprint y";
            var (plain, plainOutput) = TestHelper.Run("a", source, (script, scope) => script.AuditSink = null);
            var (audited, auditedOutput) = TestHelper.Run("a", source, (script, scope) => script.AuditSink = e => { });
            CollectionAssert.AreEqual(plainOutput, auditedOutput);
            Assert.AreEqual(plain.ComputeStateHash(), audited.ComputeStateHash());
        }
    }
}
