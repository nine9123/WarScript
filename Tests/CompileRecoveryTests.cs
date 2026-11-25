using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// The compiler recovers from an error at the end of the statement or
    /// definition it is in (v2 plan T3.8, several diagnostics per compile):
    /// it records the error and compiles the rest, so a failed compile lists
    /// every error the compiler finds, then those of the module-set checks,
    /// then the warnings; the entry points report the first. A statement
    /// that failed does not cause errors of its own further on.
    /// </summary>
    [TestFixture]
    public class CompileRecoveryTests
    {
        private static WarScriptLanguage Script(string source, Dictionary<string, string> files = null)
        {
            var script = new WarScriptLanguage("main", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => { });
            script.LogUnhandledErrors = false;
            return script;
        }

        private static string[] Lines(CompileResult result) =>
            result.Diagnostics.Select(d => d.Severity + " " + d.ScriptName + ":" + d.Line + ": " + d.Message).ToArray();

        [Test]
        public void ErrorsInSeveralStatements_AreAllReported_AndTheFirstIsTheEntryPointsError()
        {
            var script = Script(
                "print 1\n" +
                "print 1 - \"a\"\n" +
                "print 2\n" +
                "print \"b\" / 2\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(new[]
            {
                "Error main:2: Cannot apply '-' to number and text",
                "Error main:4: Cannot apply '/' to text and number",
            }, Lines(result));

            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual(2, run.Error.Line);
        }

        [Test]
        public void ErrorsInSeveralFunctions_AndInAClass_AreAllReported()
        {
            var script = Script(
                "fun f()\n" +
                "    print y\n" +
                "    y = 1\n" +
                "end\n" +
                "fun g(a)\n" +
                "    loop a in 0..2\n" +
                "    end\n" +
                "end\n" +
                "class C(x)\n" +
                "    fun m()\n" +
                "        return @nope\n" +
                "    end\n" +
                "    print 1 - \"a\"\n" +
                "end\n");

            CollectionAssert.AreEqual(new[]
            {
                "Error main:2: variable 'y' is read before it is assigned",
                "Error main:6: loop variable 'a' shadows a parameter of the same body",
                "Error main:11: 'nope' is not a field of class 'C'",
                "Error main:13: Cannot apply '-' to number and text",
            }, Lines(script.Compile()));
        }

        [Test]
        public void ALocalAssignedByAFailedStatement_IsNotReportedAgain()
        {
            // The assignment fails before 'y' is assigned: the read below is
            // not 'read before it is assigned', and no local is reported
            // unread in a body that had an error.
            var script = Script(
                "fun f()\n" +
                "    y = 1 - \"a\"\n" +
                "    print y\n" +
                "    unused = 2\n" +
                "end\n");

            CollectionAssert.AreEqual(new[] { "Error main:2: Cannot apply '-' to number and text" }, Lines(script.Compile()));
        }

        [Test]
        public void AGlobalAssignedByAFailedStatement_IsNotAnUndefinedVariable()
        {
            var script = Script(
                "z = 1 - \"a\"\n" +
                "print z\n" +
                "loop i in 0..2\n" +
                "    w = \"b\" / 2\n" +
                "end\n" +
                "print w\n");

            CollectionAssert.AreEqual(new[]
            {
                "Error main:1: Cannot apply '-' to number and text",
                "Error main:4: Cannot apply '/' to text and number",
            }, Lines(script.Compile()));
        }

        [Test]
        public void AfterAnErrorInsideALoop_TheBlocksAroundAreIntact()
        {
            // The loop and begin block the failed statements are in still
            // enclose the statements after them: 'break' compiles, and the
            // loop variable is still a local of the loop only.
            var script = Script(
                "fun f()\n" +
                "    loop i in 0..2\n" +
                "        begin\n" +
                "            print 1 - \"a\"\n" +
                "            break\n" +
                "        rescue e\n" +
                "            print e\n" +
                "        end\n" +
                "        print i\n" +
                "        break\n" +
                "    end\n" +
                "    loop i in 0..2\n" +
                "        print i\n" +
                "    end\n" +
                "end\n");

            CollectionAssert.AreEqual(new[] { "Error main:4: Cannot apply '-' to number and text" }, Lines(script.Compile()));
        }

        [Test]
        public void CompilerErrors_ComeBeforeTheModuleSetErrors_ThenTheWarnings()
        {
            var script = Script(
                "fun f()\n" +
                "    unused = 1\n" +
                "end\n" +
                "print nobody\n" +
                "print 1 - \"a\"\n");

            CollectionAssert.AreEqual(new[]
            {
                "Error main:5: Cannot apply '-' to number and text",
                "Error main:4: undefined variable 'nobody'",
                "Warning main:2: local 'unused' is assigned but never read",
            }, Lines(script.Compile()));
        }

        [Test]
        public void ErrorsInAnImportAndInTheMainFile_AreEachReportedInTheirFile()
        {
            var script = Script(
                "import \"lib\"\n" +
                "print 1 - \"a\"\n",
                new Dictionary<string, string>
                {
                    ["lib"] = "print \"b\" / 2\n",
                });

            CollectionAssert.AreEqual(new[]
            {
                "Error lib:1: Cannot apply '/' to text and number",
                "Error main:2: Cannot apply '-' to number and text",
            }, Lines(script.Compile()));
        }

        [Test]
        public void AParseError_AfterAnImportWithCompileErrors_IsListedAfterThem()
        {
            var script = Script(
                "import \"lib\"\n" +
                "x = (\n",
                new Dictionary<string, string>
                {
                    ["lib"] = "print \"b\" / 2\n",
                });

            var result = script.Compile();

            Assert.AreEqual(2, result.Diagnostics.Count, string.Join("\n", Lines(result)));
            Assert.AreEqual("Error lib:1: Cannot apply '/' to text and number", Lines(result)[0]);
            Assert.AreEqual("main", result.Diagnostics[1].ScriptName);
        }

        [Test]
        public void AnErrorInAnEnsureBody_CompiledTwice_IsReportedOnce()
        {
            var script = Script(
                "loop i in 0..2\n" +
                "    begin\n" +
                "        break\n" +
                "    ensure\n" +
                "        print 1 - \"a\"\n" +
                "    end\n" +
                "end\n");

            CollectionAssert.AreEqual(new[] { "Error main:5: Cannot apply '-' to number and text" }, Lines(script.Compile()));
        }

        [Test]
        public void ASourceImportOfLoadedBytecode_StillFailsAtItsFirstError()
        {
            var compiled = Script("import \"lib\"\nprint lib_value\n",
                new Dictionary<string, string> { ["lib"] = "lib_value = 1\n" });
            var bytes = new MemoryStream();
            compiled.SaveBytecode(bytes);
            bytes.Position = 0;

            var loading = Script("", new Dictionary<string, string> { ["lib"] = "lib_value = 1 - \"a\"\nprint 2 / \"b\"\n" });
            var e = Assert.Throws<InvalidDataException>(() => loading.LoadBytecode(bytes));
            StringAssert.Contains("Cannot apply '-' to number and text", e.InnerException.Message);
        }
    }
}
