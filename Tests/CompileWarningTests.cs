using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Exception;

namespace Tests
{
    /// <summary>
    /// Compile-time warnings (v2 plan T3.8): a body local that is assigned
    /// but never read (PROP-26) and unreachable code (PROP-29) are warning
    /// diagnostics of a successful compile, and <see cref="WarScriptLanguage.WarningsAsErrors"/>
    /// (PROP-31) turns every warning into an error.
    /// </summary>
    [TestFixture]
    public class CompileWarningTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(
            string source, Dictionary<string, string> files = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("warn", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => output.Add(msg));
            script.LogUnhandledErrors = false;
            return (script, output);
        }

        private static List<CompileDiagnostic> Warnings(CompileResult result) =>
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

        private const string Typo =
            "health = 100\n" +
            "fun damage(amt)\n" +
            "    helth = health - amt\n" +
            "    return health\n" +
            "end\n" +
            "print damage(30)\n";

        [Test]
        public void UnreadLocal_IsAWarning_AndTheScriptStillRuns()
        {
            var (script, output) = Script(Typo);

            var result = script.Compile();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            var w = result.Diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Warning, w.Severity);
            Assert.AreEqual("local 'helth' is assigned but never read", w.Message);
            Assert.AreEqual("warn", w.ScriptName);
            Assert.AreEqual(3, w.Line);
            Assert.AreEqual("warn:3:5: warning: local 'helth' is assigned but never read", w.ToString());

            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "100" }, output);
        }

        [Test]
        public void TheWarningIsAtTheFirstAssignment_OncePerLocal()
        {
            var (script, _) = Script(
                "fun f()\n" +
                "    if true\n" +
                "        a = 1\n" +
                "    end\n" +
                "    a = 2\n" +
                "    b = 3\n" +
                "end\n");

            var warnings = Warnings(script.Compile());

            CollectionAssert.AreEqual(new[] { "local 'a' is assigned but never read", "local 'b' is assigned but never read" },
                warnings.Select(w => w.Message));
            CollectionAssert.AreEqual(new[] { 3, 6 }, warnings.Select(w => w.Line));
        }

        [Test]
        public void EveryKindOfRead_CountsAsARead()
        {
            var (script, _) = Script(
                "fun reads(p)\n" +
                "    a = 1\n" +
                "    print a\n" +                       // plain read
                "    arr = [1, 2]\n" +
                "    arr[0] = 5\n" +                    // index write reads the array
                "    f = fun(x) return x end\n" +
                "    f(1)\n" +                         // call through a local
                "    n = 0\n" +
                "    n += 1\n" +                        // compound assignment reads it
                "    begin\n" +
                "        e1 = 1\n" +
                "    ensure\n" +
                "        print e1\n" +                  // read in an ensure block
                "    end\n" +
                "    s = []\n" +
                "    s << 1\n" +
                "    return p\n" +
                "end\n" +
                "top = 1\n" +                           // a global: never warned about
                "class C(q)\n" +
                "    field = 2\n" +                     // a field: never warned about
                "end\n" +
                "fun loops()\n" +
                "    loop i in 0..3\n" +                // loop and rescue variables are not body locals
                "    end\n" +
                "    begin\n" +
                "        raise \"x\"\n" +
                "    rescue err\n" +
                "    end\n" +
                "end\n");

            var result = script.Compile();

            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.AreEqual(0, result.Diagnostics.Count, string.Join("\n", result.Diagnostics));
        }

        [Test]
        public void ALambdasUnreadLocal_IsAWarning()
        {
            var (script, _) = Script("g = fun(x)\n    unused = x\n    return x\nend\n");

            var warnings = Warnings(script.Compile());

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("local 'unused' is assigned but never read", warnings[0].Message);
            Assert.AreEqual(2, warnings[0].Line);
        }

        [Test]
        public void AWarningInAnImport_NamesTheImport()
        {
            var files = new Dictionary<string, string> { ["lib"] = "fun helper()\n    t = 1\nend\n" };
            var (script, _) = Script("import \"lib\"\nhelper()\n", files);

            var warnings = Warnings(script.Compile());

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("lib", warnings[0].ScriptName);
            Assert.AreEqual(2, warnings[0].Line);
        }

        [Test]
        public void ACompileError_ComesFirst_ThenTheWarningsFoundBeforeIt()
        {
            var (script, _) = Script("fun f()\n    t = 1\nend\nfun g()\n    print u\n    u = 1\nend\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
            StringAssert.Contains("read before it is assigned", result.Diagnostics[0].Message);
            Assert.AreEqual(2, result.Diagnostics.Count);
            Assert.AreEqual(DiagnosticSeverity.Warning, result.Diagnostics[1].Severity);
            Assert.AreEqual("local 't' is assigned but never read", result.Diagnostics[1].Message);
        }

        [Test]
        public void ALambdaInAnEnsureBodyLeftByBreak_WarnsOnce()
        {
            // The ensure body is compiled twice (inlined where 'break' leaves
            // its block), and the lambda in it with it: one warning.
            var (script, _) = Script(
                "fun f()\n" +
                "    loop i in 0..2\n" +
                "        begin\n" +
                "            break\n" +
                "        ensure\n" +
                "            g = fun()\n" +
                "                z = 1\n" +
                "                return 0\n" +
                "            end\n" +
                "            g()\n" +
                "        end\n" +
                "    end\n" +
                "    return 0\n" +
                "end\n");

            var warnings = Warnings(script.Compile());

            Assert.AreEqual(1, warnings.Count, string.Join("; ", warnings));
            Assert.AreEqual("local 'z' is assigned but never read", warnings[0].Message);
            Assert.AreEqual(7, warnings[0].Line);
        }

        // ── Unreachable code (PROP-29) ──────────────────────────

        [Test]
        public void CodeAfterReturn_IsAWarning_AtTheFirstUnreachableStatement_OncePerBlock()
        {
            var (script, output) = Script(
                "fun f()\n" +
                "    return 1\n" +
                "    print \"a\"\n" +
                "    print \"b\"\n" +
                "end\n" +
                "print f()\n");

            var result = script.Compile();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            var w = result.Diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Warning, w.Severity);
            Assert.AreEqual("unreachable code after 'return'", w.Message);
            Assert.AreEqual(3, w.Line);
            Assert.AreEqual(5, w.Column);
            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "1" }, output);
        }

        [Test]
        public void CodeAfterABlockThatEndsWithRaise_IsNotReported()
        {
            var (script, _) = Script(
                "x = 1\n" +
                "if x > 0\n" +
                "    raise \"no\"\n" +
                "end\n" +
                "print x\n" +
                "class C(a)\n" +
                "    fun m()\n" +
                "        begin\n" +
                "            return @a\n" +
                "        ensure\n" +
                "            print \"done\"\n" +
                "        end\n" +
                "    end\n" +
                "end\n");

            CollectionAssert.IsEmpty(script.Compile().Diagnostics);
        }

        [Test]
        public void CodeAfterRaiseInAClassBody_IsAWarning()
        {
            var (script, _) = Script("class C(a)\n    raise \"no\"\n    b = 2\nend\n");

            var warnings = Warnings(script.Compile());

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual("unreachable code after 'raise'", warnings[0].Message);
            Assert.AreEqual(3, warnings[0].Line);
        }

        [Test]
        public void UnreachableCode_FailsTheCompile_UnderWarningsAsErrors()
        {
            var (script, _) = Script("loop true\n    break\n    print 1\nend\n");
            script.WarningsAsErrors = true;

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("unreachable code after 'break'", result.Diagnostics[0].Message);
            Assert.AreEqual(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
        }

        // ── Warnings as errors (PROP-31) ────────────────────────

        [Test]
        public void WarningsAsErrors_FailsTheCompile_AndInstallsNothing()
        {
            var (script, output) = Script("const LIMIT = 3\n" + Typo);
            script.WarningsAsErrors = true;

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            var d = result.Diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
            Assert.AreEqual("local 'helth' is assigned but never read", d.Message);
            Assert.AreEqual(4, d.Line);
            Assert.IsNull(script.GetFunction("damage", 1), "a failed compile installs no definitions");
            Assert.IsFalse(script.ConstantNames.Contains("LIMIT"), "nor the constants the parser added");

            var run = script.Run();
            Assert.AreEqual(ScriptStatus.Error, run.Status);
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual(4, run.Error.Line);
            StringAssert.Contains("local 'helth' is assigned but never read", run.Error.Message);
            Assert.AreEqual(0, output.Count, "nothing ran");
        }

        [Test]
        public void WarningsAsErrors_DoesNotAffectAWarningFreeScript()
        {
            var (script, output) = Script("fun f(a)\n    b = a\n    return b\nend\nprint f(2)\n");
            script.WarningsAsErrors = true;

            Assert.IsTrue(script.Compile().Success);
            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.AreEqual(new[] { "2" }, output);
        }

        [Test]
        public void WarningsAsErrors_IsReadWhenCompileRuns()
        {
            var (script, _) = Script(Typo);
            Assert.IsTrue(script.Compile().Success);

            script.WarningsAsErrors = true;
            Assert.IsTrue(script.Compile().Success, "the cached result stands");

            // Reload compiles, and throws a compile error as in v1.
            var error = Assert.Throws<CompileError>(() => script.Reload(Typo));
            StringAssert.Contains("local 'helth' is assigned but never read", error.Message);
            Assert.IsFalse(script.Compile().Success, "a new compile reads the option");
        }
    }
}
