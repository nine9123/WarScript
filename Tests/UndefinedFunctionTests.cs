using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// A call by name that no function and no global can answer is a compile
    /// error: <c>Function 'f' is not defined</c>, or <c>Function 'f' takes 1
    /// argument, not 2</c> when functions of that name take other numbers of
    /// arguments (v2 plan T3.8, DEC-21). The golden cases are
    /// <c>language/call_*.ws</c> and <c>calls_resolved_anywhere.ws</c>;
    /// these need a native, a host value, an import or a reload.
    /// </summary>
    [TestFixture]
    public class UndefinedFunctionTests
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
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("twice", new List<string> { "a" }),
                args => WarValue.FromNumeric(args[0].Numeric * 2), "", ""));
            return (script, output);
        }

        [Test]
        public void AnUndefinedCall_IsACompileError_AtTheName_AndInstallsNothing()
        {
            var (script, output) = Script(
                "fun helper []\n" +
                "end\n" +
                "x = 1 + nope [2]\n");

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Function 'nope' is not defined", result.Diagnostics[0].Message);
            Assert.AreEqual(3, result.Diagnostics[0].Line);
            Assert.AreEqual(9, result.Diagnostics[0].Column, "at the name");
            Assert.IsNull(script.GetFunction("helper", 0));
            var run = script.Run();
            Assert.AreEqual(ScriptErrorKind.CompileError, run.Error.Kind);
            CollectionAssert.IsEmpty(output, "nothing ran");
        }

        [Test]
        public void ANative_IsKnown_WithItsArity()
        {
            var (script, output) = Script("print twice [3]\n");
            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "6" }, output);

            var (wrong, _) = Script("print twice [3, 4]\n");
            Assert.AreEqual("Function 'twice' takes 1 argument, not 2", wrong.Compile().Diagnostics[0].Message);
        }

        [Test]
        public void AHostGlobal_MayHoldAFunctionValue()
        {
            // Any value: the VM's fallback decides at runtime.
            var (script, output) = Script(
                "begin\n" +
                "    HOST_HOOK [1]\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n");
            script.GlobalMemoryScope.Set("HOST_HOOK", WarValue.FromNumeric(FixMath.F64.FromInt(3)));

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "Function 'HOST_HOOK' with 1 args is not defined" }, output);
        }

        [Test]
        public void AGlobalTheInstanceHolds_IsKnown_AfterAReload()
        {
            var (script, output) = Script("op = fun [x] return x + 1 end\n");
            Assert.IsTrue(script.Run().IsOk);

            script.Reload("print op [4]\n");

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void AFunctionOfTheImporter_IsKnownToTheImport_AndTheOtherWayRound()
        {
            var files = new Dictionary<string, string>
            {
                ["lib"] = "fun from_lib []\n    return from_main [] + 1\nend\n",
            };
            var (script, output) = Script(
                "import \"lib\"\n" +
                "fun from_main []\n    return 3\nend\n" +
                "print from_lib []\n", files);

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "4" }, output);
        }

        [Test]
        public void AnUndefinedCallInAnImport_IsReportedInTheImport()
        {
            var files = new Dictionary<string, string> { ["lib"] = "\ny = missing []\n" };
            var (script, _) = Script("import \"lib\"\n", files);

            var result = script.Compile();

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Function 'missing' is not defined", result.Diagnostics[0].Message);
            Assert.AreEqual("lib", result.Diagnostics[0].ScriptName);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
        }

        [Test]
        public void WithABytecodeImport_NothingIsChecked_AndTheVMStillRaises()
        {
            // Which globals a bytecode import writes (one may hold a function
            // value) is not known at compile time, so the call compiles; the
            // VM is the backstop.
            var lib = new WarScriptLanguage("lib", "fun known [] return 1 end\n", null, null);
            var stream = new System.IO.MemoryStream();
            lib.SaveBytecode(stream);
            var bytes = stream.ToArray();
            var (script, output) = Script(
                "import \"lib\"\n" +
                "print known []\n" +
                "begin\n" +
                "    unknown []\n" +
                "rescue e\n" +
                "    print e\n" +
                "end\n",
                bytecode: path => path == "lib" ? bytes : null);

            Assert.IsTrue(script.Run().IsOk);
            Assert.AreEqual(new[] { "1", "Function 'unknown' with 0 args is not defined" }, output);
        }

        [Test]
        public void ATailCall_IsChecked()
        {
            var (script, _) = Script(
                "fun f [n]\n" +
                "    return g [n]\n" +
                "end\n");
            var result = script.Compile();
            Assert.AreEqual("Function 'g' is not defined", result.Diagnostics[0].Message);
            Assert.AreEqual(2, result.Diagnostics[0].Line);
        }
    }
}
