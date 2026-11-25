using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;

namespace Tests
{
    /// <summary>
    /// The checks that run once the whole module set is compiled (unknown
    /// classes, <c>::</c> names, undefined globals, calls by name, constant
    /// writes; v2 plan T3.8) report every error they find, not only the
    /// first: a failed compile lists them all, then its warnings, and the
    /// entry points report the first.
    /// </summary>
    [TestFixture]
    public class ModuleSetErrorTests
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
        public void EveryError_IsReported_InCheckOrder_ThenTheWarnings()
        {
            var script = Script(
                "fun g()\n" +
                "    global MAX\n" +
                "    MAX = 2\n" +
                "end\n" +
                "fun f()\n" +
                "    unused = 1\n" +
                "end\n" +
                "a = new Nope()\n" +
                "print nobody\n" +
                "print missing(1)\n" +
                "print nobody2\n" +
                "const MAX = 1\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            CollectionAssert.AreEqual(new[]
            {
                "Error main:8: Class 'Nope' is not defined",
                "Error main:9: undefined variable 'nobody'",
                "Error main:11: undefined variable 'nobody2'",
                "Error main:10: Function 'missing' is not defined",
                "Error main:3: Cannot reassign constant 'MAX'",
                "Warning main:6: local 'unused' is assigned but never read",
            }, Lines(result));

            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            Assert.AreEqual("Class 'Nope' is not defined", run.Error.Message);
            Assert.AreEqual(8, run.Error.Line);
        }

        [Test]
        public void ErrorsInSeveralFiles_AreEachReportedInTheirFile()
        {
            var script = Script(
                "import \"lib\"\n" +
                "print lib_value\n" +
                "print main_missing\n",
                new Dictionary<string, string>
                {
                    ["lib"] = "lib_value = 1\nprint lib_missing\n",
                });

            var result = script.Compile();

            CollectionAssert.AreEqual(new[]
            {
                "Error lib:2: undefined variable 'lib_missing'",
                "Error main:3: undefined variable 'main_missing'",
            }, Lines(result));
        }

        [Test]
        public void AUseInAnEnsureBody_CompiledTwice_IsReportedOnce()
        {
            // 'break' inlines the ensure body where it leaves the block, so
            // its read of 'nobody' is compiled twice.
            var script = Script(
                "loop i in 0..2\n" +
                "    begin\n" +
                "        break\n" +
                "    ensure\n" +
                "        print nobody\n" +
                "    end\n" +
                "end\n");

            CollectionAssert.AreEqual(new[] { "Error main:5: undefined variable 'nobody'" }, Lines(script.Compile()));
        }

        [Test]
        public void AFailedCompile_InstallsNothing_AndAFixedReloadCompiles()
        {
            var script = Script("fun helper()\nend\nprint a\nprint b\n");
            Assert.AreEqual(2, script.Compile().Diagnostics.Count);
            Assert.IsNull(script.GetFunction("helper", 0));

            script.Reload("fun helper()\nend\na = 1\nprint a\n");
            Assert.IsTrue(script.Compile().Success);
            Assert.IsNotNull(script.GetFunction("helper", 0));
        }
    }
}
