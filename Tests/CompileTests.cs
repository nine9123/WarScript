using System.Collections.Generic;
using System.IO;
using System.Linq;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// The explicit compile step (v2 plan T2.2, 6.1, 6.2): <c>Compile()</c>
    /// lexes, parses and compiles without running code, returns diagnostics
    /// instead of throwing, and is idempotent; the entry points compile on
    /// demand and, until result types (T2.3), throw a compile error.
    /// </summary>
    [TestFixture]
    public class CompileTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("compile", source, null, (s, msg) => output.Add(msg));
            return (script, output);
        }

        private const string Valid = "print \"top\"\nfun f [a]\n    return a + 1\nend\n";

        // ── Success ─────────────────────────────────────────────

        [Test]
        public void Compile_CompilesWithoutRunningCode()
        {
            var (script, output) = Script(Valid);

            var result = script.Compile();

            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.AreEqual(0, output.Count, "Compile runs no top-level code");
            var f = script.GetFunction("f", 1);
            Assert.IsNotNull(f);
            Assert.IsNotNull(f.Compiled);
        }

        [Test]
        public void Compile_IsIdempotent_AndRunReusesIt()
        {
            var (script, output) = Script(Valid);
            var first = script.Compile();
            var compiled = script.GetFunction("f", 1).Compiled;

            Assert.AreSame(first, script.Compile());
            script.Run();

            Assert.AreSame(first, script.Compile());
            Assert.AreSame(compiled, script.GetFunction("f", 1).Compiled, "Run does not compile again");
            CollectionAssert.AreEqual(new[] { "top" }, output);
        }

        [Test]
        public void Call_AfterCompile_RunsTheFunction()
        {
            var (script, output) = Script("fun f [a]\n    print a * 2\nend\n");
            Assert.IsTrue(script.Compile().Success);

            script.Call(script.GetFunction("f", 1), WarValue.FromNumeric(21));

            CollectionAssert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void StartCoroutine_BeforeRun_CompilesTheScript()
        {
            var (script, output) = Script("print \"top\"\nfun co []\n    print \"a\"\n    yield\n    print \"b\"\nend\n");

            var id = script.StartCoroutine("co", null);

            Assert.Greater(id, 0);
            Assert.IsFalse(script.ExceptionContext.IsRaised());
            CollectionAssert.AreEqual(new[] { "a" }, output, "the top-level code has not run");
            script.TickCoroutines(F64.Zero);
            CollectionAssert.AreEqual(new[] { "a", "b" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        // ── Failure ─────────────────────────────────────────────

        [Test]
        public void LexError_IsADiagnostic_NotThrown()
        {
            WarScriptLanguage script = null;
            Assert.DoesNotThrow(() => script = new WarScriptLanguage("compile", "x = 1\ny = é", null, null),
                "the constructor does not lex");

            CompileResult result = null;
            Assert.DoesNotThrow(() => result = script.Compile());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            var d = result.Diagnostics[0];
            Assert.AreEqual(DiagnosticSeverity.Error, d.Severity);
            Assert.AreEqual("compile", d.ScriptName);
            StringAssert.Contains("Unexpected character 'é' at line 2", d.Message);
        }

        [Test]
        public void ParseError_IsADiagnostic()
        {
            var (script, _) = Script("x = 1\nif x\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, result.Diagnostics.Count);
            Assert.AreEqual(DiagnosticSeverity.Error, result.Diagnostics[0].Severity);
            Assert.IsNotEmpty(result.Diagnostics[0].Message);
        }

        [Test]
        public void CompilerLimitError_CarriesItsReasonAndLine()
        {
            var args = string.Join(", ", Enumerable.Range(0, 256).Select(i => "1"));
            var (script, _) = Script("fun f [a]\nend\nx = 1\nf [" + args + "]\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            var d = result.Diagnostics.Single();
            Assert.AreEqual("more than 255 arguments in a call", d.Message);
            Assert.AreEqual("compile", d.ScriptName);
            Assert.AreEqual(4, d.Line);
            Assert.AreEqual(0, d.Column);
            Assert.AreEqual("compile:4:0: error: more than 255 arguments in a call", d.ToString());
        }

        [Test]
        public void FailedCompile_IsCached_AndLeavesNoDefinitions()
        {
            var (script, output) = Script("fun f []\n    return 1\nend\nprint \"top\"\nx = (\n");

            var first = script.Compile();

            Assert.IsFalse(first.Success);
            Assert.AreSame(first, script.Compile());
            Assert.IsNull(script.GetFunction("f", 0), "a definition without bytecode is not reachable");
            Assert.AreEqual(0, output.Count);
        }

        [Test]
        public void FailedCompile_LeavesNoConstantNames_ButKeepsTheHostOnes()
        {
            var (script, _) = Script("const MAX = 1\nenum E\n    A\nend\nx = (\n");
            script.ConstantNames.Add("HOST");

            Assert.IsFalse(script.Compile().Success);

            CollectionAssert.AreEquivalent(new[] { "HOST" }, script.ConstantNames);
        }

        [Test]
        public void EntryPoints_ReturnTheCompileError_WithContextsRestored()
        {
            var (script, output) = Script("print \"top\"\nfun co []\n    yield\nend\nx = (\n");
            var before = Invariants.Capture(script);
            var diagnostic = script.Compile().Diagnostics[0];

            var run = script.Run();
            Assert.AreEqual(ScriptStatus.Error, run.Status);
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual(diagnostic.Message, run.Error.Message);
            Assert.AreEqual(diagnostic.ScriptName, run.Error.ScriptName);
            Assert.IsTrue(run.Value.IsNull);
            // The compile error halted the instance (T2.4): later entries
            // answer Halted with the same error and log nothing.
            Assert.IsTrue(script.IsHalted);
            var start = script.StartCoroutine("co", null, false, out var id);
            Assert.AreEqual(ScriptStatus.Halted, start.Status);
            Assert.AreSame(run.Error, start.Error);
            Assert.AreEqual(-1, id);

            CollectionAssert.AreEqual(new[] { "[WarScript] " + run.Error }, output,
                "nothing runs; the halting error is logged once");
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            Assert.IsFalse(script.ExceptionContext.IsRaised());
            Assert.IsNull(Invariants.Violation(script, before));
        }

        // ── Reload and LoadBytecode ─────────────────────────────

        [Test]
        public void Reload_ReplacesTheCompileResult()
        {
            var (script, output) = Script(Valid);
            script.Run();
            var ok = script.Compile();

            Assert.Throws<SyntaxException>(() => script.Reload("fun g [\n"));
            var failed = script.Compile();
            Assert.IsFalse(failed.Success);
            Assert.IsNull(script.GetFunction("f", 1));

            script.Reload("fun g []\n    print \"g\"\nend\n");
            var again = script.Compile();
            Assert.IsTrue(again.Success);
            Assert.AreNotSame(failed, again);
            script.Call(script.GetFunction("g", 0));
            CollectionAssert.AreEqual(new[] { "top", "g" }, output);
            Assert.IsTrue(ok.Success);
        }

        [Test]
        public void LoadBytecode_CountsAsCompiled_EvenAfterAFailedCompile()
        {
            var (compiler, _) = Script(Valid);
            var bytes = new MemoryStream();
            compiler.SaveBytecode(bytes);

            var (script, output) = Script("x = (\n");
            Assert.IsFalse(script.Compile().Success);
            bytes.Position = 0;
            script.LoadBytecode(bytes);

            Assert.IsTrue(script.Compile().Success);
            script.Run();
            CollectionAssert.AreEqual(new[] { "top" }, output);
        }
    }
}
