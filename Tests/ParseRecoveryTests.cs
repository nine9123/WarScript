using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Exception;

namespace Tests
{
    /// <summary>
    /// The parser recovers from a syntax error by skipping the statement it
    /// is in (v2 plan T3.8, several diagnostics per compile): a failed
    /// compile lists every syntax error of the file, and the file is then
    /// not compiled, so the code skipped causes no errors of its own. The
    /// entry points report the first.
    /// </summary>
    [TestFixture]
    public class ParseRecoveryTests
    {
        private static WarScriptLanguage Script(string source, Dictionary<string, string> files = null)
        {
            var script = new WarScriptLanguage("main", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => { });
            script.LogUnhandledErrors = false;
            return script;
        }

        private static string[] Positions(CompileResult result) =>
            result.Diagnostics.Select(d => d.Severity + " " + d.ScriptName + ":" + d.Line + ":" + d.Column).ToArray();

        [Test]
        public void SyntaxErrorsInSeveralStatements_AreAllReported_AndTheFileIsNotCompiled()
        {
            // Line 2 would be a compile error: the file is not compiled once
            // it has a syntax error.
            var script = Script(
                "x = (\n" +
                "print 1 - \"a\"\n" +
                "print 2\n" +
                "z = ]\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(new[] { "Error main:1:5", "Error main:4:3" }, Positions(result));
            StringAssert.Contains("Unbalanced '('", result.Diagnostics[0].Message);

            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual(1, run.Error.Line);
            Assert.Throws<SyntaxException>(() => script.CompileWithoutRunning());
        }

        [Test]
        public void AnErrorInABlock_IsRecoveredInTheBlock()
        {
            var script = Script(
                "if true\n" +
                "    x = (\n" +
                "    print 1\n" +
                "end\n" +
                "print )\n");

            CollectionAssert.AreEqual(new[] { "Error main:2:9", "Error main:5:7" }, Positions(script.Compile()));
        }

        [Test]
        public void AnErrorInABlockHeader_SkipsTheWholeBlock()
        {
            // The block's own errors are not reported: its statements are
            // skipped with it, to its 'end'.
            var script = Script(
                "if (\n" +
                "    x = (\n" +
                "elif true\n" +
                "    loop i in 0..2\n" +
                "    end\n" +
                "else\n" +
                "    y = ]\n" +
                "end\n" +
                "print )\n");

            CollectionAssert.AreEqual(new[] { "Error main:1:4", "Error main:9:7" }, Positions(script.Compile()));
        }

        [Test]
        public void ErrorsInFunctionsAndLambdas_AreAllReported()
        {
            var script = Script(
                "fun f [a]\n" +
                "    return (\n" +
                "    print a\n" +
                "end\n" +
                "fun g [b, b]\n" +
                "    print ]\n" +
                "end\n" +
                "h = fun [x]\n" +
                "    y = (\n" +
                "    return x\n" +
                "end\n" +
                "print )\n");

            var result = script.Compile();

            CollectionAssert.AreEqual(new[]
            {
                "Error main:2:12", "Error main:5:11", "Error main:9:9", "Error main:12:7",
            }, Positions(result));
            Assert.AreEqual("Parameter 'b' is declared twice", result.Diagnostics[1].Message);
        }

        [Test]
        public void StrayClosingKeywordsAtTheTopLevel_AreEachReported()
        {
            var script = Script(
                "print 1\n" +
                "end\n" +
                "print 2\n" +
                "else\n" +
                "x = (\n");

            var result = script.Compile();

            CollectionAssert.AreEqual(new[] { "Error main:2:1", "Error main:4:1", "Error main:5:5" }, Positions(result));
            StringAssert.StartsWith("Unexpected `end`", result.Diagnostics[0].Message);
        }

        [Test]
        public void StatementsSpanningLines_AfterAnError_AreParsedWhole()
        {
            // A multi-line array literal and a lambda argument after the
            // failed statement cause no errors of their own.
            var script = Script(
                "x = (\n" +
                "y = {1,\n" +
                "     2}\n" +
                "z = Array_map [{1}, fun [v]\n" +
                "    return v\n" +
                "end]\n" +
                "w = ]\n");

            CollectionAssert.AreEqual(new[] { "Error main:1:5", "Error main:7:3" }, Positions(script.Compile()));
        }

        [Test]
        public void AnUnclosedBracket_EndsAtTheNextStatementLine()
        {
            var script = Script(
                "fun f [a]\n" +
                "    return a\n" +
                "end\n" +
                "x = f [1\n" +
                "if true\n" +
                "    print 1\n" +
                "end\n" +
                "w = ]\n");

            var result = script.Compile();

            Assert.AreEqual(2, result.Diagnostics.Count, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
            Assert.AreEqual(8, result.Diagnostics[1].Line);
        }

        [Test]
        public void ABlockNeverClosed_IsReportedOnce()
        {
            var script = Script(
                "print )\n" +
                "loop i in 0..3\n" +
                "    print i\n");

            var result = script.Compile();

            Assert.AreEqual(2, result.Diagnostics.Count, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
            Assert.AreEqual(1, result.Diagnostics[0].Line);
        }

        [Test]
        public void SyntaxErrorsInAnImport_AreAllReported_AndTheImporterIsNotParsed()
        {
            var script = Script(
                "import \"lib\"\n" +
                "print )\n",
                new Dictionary<string, string> { ["lib"] = "x = (\nprint ]\n" });

            CollectionAssert.AreEqual(new[] { "Error lib:1:5", "Error lib:2:7" }, Positions(script.Compile()));
        }

        [Test]
        public void AFailedCompile_PutsTheConstantsBack()
        {
            var script = Script(
                "const K = 1\n" +
                "x = (\n" +
                "const L = 2\n" +
                "y = ]\n");

            Assert.AreEqual(2, script.Compile().Diagnostics.Count);
            Assert.IsFalse(script.ConstantNames.Contains("K"));
            Assert.IsFalse(script.ConstantNames.Contains("L"));
        }
    }
}
