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

        // MixedTypeEquality, MixedTypeOrdering, LogicalOrdering,
        // TextSubtraction, NonTextConcatenation, LogicalFormatted,
        // NegateNonNumeric, ArrayPlusValue and AppendToNonArray have no v1
        // path left since T3.6: the operators follow the table of plan 5.3
        // and logical values print as true/false.
        // IndexReadInvalid, IndexWriteInvalid, TextIndexAssign and
        // PropertyWriteOnNonInstance have no v1 path left since T3.6: those
        // reads and writes raise IndexError or TypeError (5.5).
        [TestCase("class P [x]\nend\np = new P [1]\np :: z = 4", AuditKind.DynamicPropertyCreated, 4)]
        [TestCase("r = undefined_thing", AuditKind.UndefinedRead, 1)]
        // GlobalWriteFromFunction, FieldAccessViaBareName, GlobalShadowedInClass
        // and BlockVariableEscapes have no v1 path left since T3.4: a name a
        // function or method assigns is its local, a global needs `global`,
        // methods and class bodies read globals, top-level blocks have no
        // scope of their own, and `this :: z = 1` in a method is a compile
        // error unless z is a field (V2NameResolutionReportsNothing).
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
        public void V2NameResolutionReportsNothing()
        {
            var events = Audit("g = 1\nfun f []\n  global g\n  g = 2\nend\nf []\n"
                + "h = fun [] global g g = 3 end\nh []\n"
                + "class P [x]\n  fun m []\n    print g\n    x = 2\n    print @x\n  end\nend\np = new P [1]\np :: m []\n"
                + "class Q [x]\n  y = g\nend\nq = new Q [1]\n"
                + "if true\n  inner = 7\nend\nprint inner\n"
                + "begin\n  inner2 = 7\n  raise \"x\"\nrescue e\nend\nprint inner2");
            Assert.AreEqual(0, events.Count, "events:\n" + Format(events));
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
                ["lib"] = "class P [x]\nend\nfun same [a, b]\n  return a == b\nend\na1 = new P [1]\na2 = new P [1]\nx = a1 == a2"
            };
            var events = Audit("import \"lib\"\ny = same [new P [2], new P [2]]", path => files.TryGetValue(path, out var s) ? s : null);

            var topLevel = events.Single(e => e.Kind == AuditKind.ClassStructuralEquality && e.Line == 8);
            Assert.AreEqual("lib", topLevel.Script);

            var inFunction = events.Single(e => e.Kind == AuditKind.ClassStructuralEquality && e.Line == 4);
            Assert.AreEqual("lib", inFunction.Script, "a function defined in the import, called from the entry script");
            Assert.AreEqual("same", inFunction.Function);
        }

        [Test]
        public void ReportGroupsEventsBySite()
        {
            var report = new AuditReport();
            TestHelper.Run("main", "class P [x]\nend\na = new P [1]\nb = new P [1]\nloop i in 0..3\n  c = a == b\nend\nr = undefined_thing",
                           (script, scope) => { script.AuditSink = report.Add; script.LogUnhandledErrors = false; });

            Assert.AreEqual(4, report.EventCount);
            var entries = report.Entries;
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(AuditKind.UndefinedRead, entries[0].Kind);
            Assert.AreEqual(AuditKind.ClassStructuralEquality, entries[1].Kind);
            Assert.AreEqual(6, entries[1].Line);
            Assert.AreEqual(3, entries[1].Count);
            Assert.AreEqual("P instance == P instance: two instances, structurally equal", entries[1].FirstDetail);

            var markdown = report.ToMarkdown("Test");
            StringAssert.StartsWith("# Test\n\nEvents: 4, sites (kind, script, line): 2.\n", markdown);
            StringAssert.Contains("| `ClassStructuralEquality` | 1 | 3 |", markdown);
            StringAssert.Contains("| `MixedTypeEquality` | 0 | 0 |", markdown);
            StringAssert.Contains("| main:6 | <script> | 3 | P instance == P instance: two instances, structurally equal |", markdown);

            var json = report.ToJson();
            StringAssert.StartsWith("{\"events\": 4, \"sites\": [\n", json);
            StringAssert.Contains("{\"kind\": \"ClassStructuralEquality\", \"script\": \"main\", \"line\": 6, \"count\": 3, " +
                                  "\"functions\": [\"<script>\"], \"first\": \"P instance == P instance: two instances, structurally equal\"}", json);
        }

        [Test]
        public void AuditingDoesNotChangeTheRun()
        {
            const string source = "class P [x]\nend\nx = {1, true}\nprint x\nprint new P [1] == new P [1]\ny = undefined_thing";
            var (plain, plainOutput) = TestHelper.Run("a", source, (script, scope) => script.AuditSink = null);
            var (audited, auditedOutput) = TestHelper.Run("a", source, (script, scope) => script.AuditSink = e => { });
            CollectionAssert.AreEqual(plainOutput, auditedOutput);
            Assert.AreEqual(plain.ComputeStateHash(), audited.ComputeStateHash());
        }
    }
}
