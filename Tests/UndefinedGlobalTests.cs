using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// A read of a global that no file of the module set writes and the host
    /// does not define is the compile error <c>undefined variable 'x'</c>
    /// (v2 plan T3.8, PROP-27, DEC-21). The golden cases are
    /// <c>language/undefined_global_*.ws</c>,
    /// <c>undefined_field_by_bare_name.ws</c> and
    /// <c>global_reads_written_anywhere.ws</c>; these need a host value, an
    /// import or a reload.
    /// </summary>
    [TestFixture]
    public class UndefinedGlobalTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(
            string source, Dictionary<string, string> files = null,
            System.Func<string, byte[]> bytecode = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source,
                files == null ? null : path => files.TryGetValue(path, out var text) ? text : null,
                (s, msg) => output.Add(msg), bytecode);
            script.LogUnhandledErrors = false;
            return (script, output);
        }

        [Test]
        public void AnUndefinedRead_IsACompileError_AtTheName_AndInstallsNothing()
        {
            var (script, output) = Script(
                "fun helper []\n" +
                "end\n" +
                "x = 1 + nope\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("undefined variable 'nope'", result.Diagnostics[0].Message);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
            Assert.AreEqual(9, result.Diagnostics[0].Column, "at the name");
            Assert.IsNull(script.GetFunction("helper", 0));
            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            CollectionAssert.IsEmpty(output, "nothing ran");
        }

        [Test]
        public void AHostGlobal_IsKnown()
        {
            var (script, output) = Script("print HOST_SPEED * 2\n");
            script.GlobalMemoryScope.Set("HOST_SPEED", WarValue.FromNumeric(FixMath.F64.FromInt(3)));

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "6" }, output);
        }

        [Test]
        public void AGlobalTheInstanceHolds_IsKnown_AfterAReload()
        {
            var (script, output) = Script("kept = 4\n");
            Assert.IsTrue(script.Run().IsOk);

            script.Reload("print kept\n");

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "4" }, output);
        }

        [Test]
        public void AGlobalWrittenByAnImport_IsKnownToTheImporter_AndTheOtherWayRound()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "shared = 2\nfun from_main []\n    return main_value\nend\n",
            };
            var (script, output) = Script(
                "import \"lib\"\n" +
                "main_value = 3\n" +
                "print shared + from_main []\n", files);

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void AnUndefinedReadInAnImport_IsReportedInTheImport()
        {
            var files = new Dictionary<string, string> { ["lib"] = "\ny = missing\n" };
            var (script, _) = Script("import \"lib\"\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("undefined variable 'missing'", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
        }

        [Test]
        public void WithABytecodeImport_NothingIsChecked_AndTheVMStillRaises()
        {
            // Which globals a bytecode import writes is not known at compile
            // time, so the read compiles; the VM is the backstop.
            var lib = new WarScriptLanguage("lib", "written = 1\n", null, null);
            var stream = new System.IO.MemoryStream();
            lib.SaveBytecode(stream);
            var bytes = stream.ToArray();
            var (script, output) = Script(
                "import \"lib\"\n" +
                "print written\n" +
                "begin\n" +
                "    print unknown\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n",
                bytecode: path => path == "lib" ? bytes : null);

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "1", "undefined variable 'unknown'" }, output);
        }

        [Test]
        public void AFieldOfADerivedClass_IsKnownInABaseClassBody_ButNotInAMethod()
        {
            var (script, output) = Script(
                "class Base []\n" +
                "    copy = speed\n" +
                "end\n" +
                "class Fast [speed] : Base []\n" +
                "end\n" +
                "print (new Fast [9]) :: copy\n");
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "9" }, output);

            var (method, _) = Script(
                "class Fast [speed]\n" +
                "    fun get []\n" +
                "        return speed\n" +
                "    end\n" +
                "end\n");
            Assert.AreEqual("undefined variable 'speed'", method.Compile().Diagnostics[0].Message);
        }
    }
}
