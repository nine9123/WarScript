using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Instance label (v2 plan T2.8, PROP-4): <c>InstanceLabel</c> is carried
    /// by every <c>ScriptError</c> and prefixes every line the runtime logs;
    /// <c>print</c> output is unchanged.
    /// </summary>
    [TestFixture]
    public class InstanceLabelTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source, string label = "unit-7")
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source, null, (s, m) => output.Add(m));
            script.InstanceLabel = label;
            return (script, output);
        }

        [Test]
        public void Label_IsNullByDefault_AndLogLinesAreUnchanged()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", "x = 1 / 0\n", null, (s, m) => output.Add(m));

            var result = script.Run();

            Assert.IsNull(script.InstanceLabel);
            Assert.IsNull(result.Error.InstanceLabel);
            CollectionAssert.AreEqual(new[] { "[WarScript] " + result.Error }, output);
        }

        [Test]
        public void RuntimeError_CarriesTheLabel_AndItsLogLineStartsWithIt()
        {
            var (script, output) = Script("print \"before\"\nx = 1 / 0\n");

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            Assert.AreEqual("unit-7", result.Error.InstanceLabel);
            StringAssert.StartsWith("main:2:0: DivisionByZero: Division by zero", result.Error.ToString(),
                "ToString keeps the 6.1 format; the label is a separate property");
            CollectionAssert.AreEqual(new[] { "before", "[unit-7] [WarScript] " + result.Error }, output);
        }

        [Test]
        public void Print_IsNotPrefixed()
        {
            var (script, output) = Script("print \"hello\"\n");

            Assert.IsTrue(script.Run().IsOk);

            CollectionAssert.AreEqual(new[] { "hello" }, output);
        }

        [Test]
        public void CompileError_CarriesTheLabel()
        {
            var (script, output) = Script("fun f [\n");

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.CompileError, result.Error.Kind);
            Assert.AreEqual("unit-7", result.Error.InstanceLabel);
            Assert.AreEqual(1, output.Count);
            StringAssert.StartsWith("[unit-7] [WarScript] ", output[0]);
        }

        [Test]
        public void HostFault_CarriesTheLabel()
        {
            var (script, output) = Script("fun f []\n    x = 1\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            script.AddBreakpoint(2);
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");

            var result = script.Call(script.GetFunction("f", 0));

            Assert.AreEqual(ScriptStatus.HostFault, result.Status);
            Assert.AreEqual("unit-7", result.Error.InstanceLabel);
            Assert.AreEqual(1, output.Count);
            StringAssert.StartsWith("[unit-7] [WarScript] main:0:0: HostFault: host failed", output[0]);
        }

        [Test]
        public void RefusedEntry_CarriesTheLabel()
        {
            var (script, _) = Script("fun f []\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            var f = script.GetFunction("f", 0);
            script.Reload("fun g []\nend\n");

            var result = script.Call(f);

            Assert.AreEqual(ScriptErrorKind.NameError, result.Error.Kind);
            Assert.AreEqual("unit-7", result.Error.InstanceLabel);
        }

        [Test]
        public void HaltedResult_IsTheFaultWithTheLabelItWasReportedWith()
        {
            var (script, _) = Script("fun f []\n    return 1 / 0\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            var f = script.GetFunction("f", 0);
            Assert.AreEqual(ScriptStatus.Error, script.Call(f).Status);

            script.InstanceLabel = "renamed";
            var halted = script.Call(f);

            Assert.AreEqual(ScriptStatus.Halted, halted.Status);
            Assert.AreEqual("unit-7", halted.Error.InstanceLabel);
            script.ClearFault();
            Assert.AreEqual("renamed", script.Call(f).Error.InstanceLabel);
        }

        [Test]
        public void FaultedHandler_SeesTheLabel()
        {
            var (script, _) = Script("x = 1 / 0\n");
            string seen = null;
            script.Faulted += (s, e) => seen = s.InstanceLabel + "/" + e.InstanceLabel;

            script.Run();

            Assert.AreEqual("unit-7/unit-7", seen);
        }

        [Test]
        public void ShadowingANativeLogLine_IsPrefixed()
        {
            var (script, output) = Script("fun twice [a]\n    return a\nend\n");
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("twice", new List<string> { "a" }), args => args[0], "", ""));

            Assert.IsTrue(script.Run().IsOk);

            CollectionAssert.AreEqual(new[] { "[unit-7] Shadowing native function 'twice'" }, output);
        }

        [Test]
        public void UnloadableImportBytecode_LogLineIsPrefixed()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", "import \"lib\"\nprint value\n",
                path => "value = 3\n", (s, m) => output.Add(m),
                path => Encoding.UTF8.GetBytes("not bytecode"));
            script.InstanceLabel = "room-1";

            Assert.IsTrue(script.Run().IsOk);

            Assert.AreEqual(2, output.Count, string.Join("\n", output));
            StringAssert.StartsWith("[room-1] [WarScript] Precompiled bytecode for import 'lib'", output[0]);
            Assert.AreEqual("3", output[1]);
        }
    }
}
