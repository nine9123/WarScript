using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Function handles (v2 plan T2.6, API-4): <c>GetFunction</c> returns a
    /// handle by name and arity that calls whatever the instance defines
    /// under them at the time of the call, so it survives <c>Reload</c>,
    /// <c>LoadBytecode</c> and new definitions.
    /// </summary>
    [TestFixture]
    public class FunctionHandleTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source, null, (s, m) => output.Add(m));
            script.LogUnhandledErrors = false;
            return (script, output);
        }

        private static void AddNative(WarScriptLanguage script, string name, int arity, Func<List<WarValue>, WarValue> body)
        {
            var parameters = new List<string>();
            for (var i = 0; i < arity; i++) parameters.Add("a" + i);
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, parameters), body, "", ""));
        }

        private static string Text(ScriptResult result)
        {
            Assert.AreEqual(ScriptStatus.Ok, result.Status, result.ToString());
            return result.Value.TextValue;
        }

        [Test]
        public void Handle_HasItsNameArityAndInstance()
        {
            var (script, _) = Script("fun f [a, b]\nend\n");
            Assert.IsTrue(script.Compile().Success);

            var f = script.GetFunction("f", 2);

            Assert.AreEqual("f", f.Name);
            Assert.AreEqual(2, f.Arity);
            Assert.AreSame(script, f.Instance);
            Assert.IsTrue(f.IsDefined);
            Assert.AreEqual("f/2", f.ToString());
        }

        [Test]
        public void GetFunction_ReturnsNull_WhenNothingIsDefined()
        {
            var (script, _) = Script("fun f [a]\nend\n");
            Assert.IsNull(script.GetFunction("f", 1), "not compiled yet");
            Assert.IsTrue(script.Compile().Success);

            Assert.IsNull(script.GetFunction("f", 2));
            Assert.IsNull(script.GetFunction("g", 1));
        }

        [Test]
        public void Handle_SurvivesReload_AndCallsTheNewDefinition()
        {
            var (script, _) = Script("fun format [x]\n    return \"v1: \" + x\nend\n");
            script.Run();
            var format = script.GetFunction("format", 1);
            Assert.AreEqual("v1: 42", Text(script.Call(format, WarValue.FromNumeric(42))));

            script.Reload("fun format [x]\n    return \"v2: \" + x\nend\n");

            Assert.AreEqual("v2: 42", Text(script.Call(format, WarValue.FromNumeric(42))));
        }

        [Test]
        public void Handle_ToAFunctionTheReloadRemoved_IsANameError_AndWorksAgainOnceItIsBack()
        {
            var (script, _) = Script("fun tick []\n    return \"a\"\nend\n");
            script.Run();
            var tick = script.GetFunction("tick", 0);

            script.Reload("fun other []\nend\n");
            Assert.IsFalse(tick.IsDefined);
            var result = script.Call(tick);

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.AreEqual(ScriptErrorKind.NameError, result.Error.Kind);
            Assert.AreEqual("Function 'tick' with 0 args is not defined", result.Error.Message);
            Assert.IsTrue(script.IsHalted, "an outermost entry that cannot start halts the instance (plan 6.4)");

            script.Reload("fun tick []\n    return \"b\"\nend\n");

            Assert.IsTrue(tick.IsDefined);
            Assert.AreEqual("b", Text(script.Call(tick)));
        }

        [Test]
        public void Handle_ToADefaultParameterArity_FollowsTheReload()
        {
            var (script, _) = Script("fun f [a, b = 1]\n    return a + b\nend\n");
            script.Run();
            var f1 = script.GetFunction("f", 1);
            Assert.AreEqual(3, Text3(script.Call(f1, WarValue.FromNumeric(2))));

            script.Reload("fun f [a]\n    return a * 10\nend\n");

            Assert.AreEqual(20, Text3(script.Call(f1, WarValue.FromNumeric(2))));
        }

        private static int Text3(ScriptResult result)
        {
            Assert.AreEqual(ScriptStatus.Ok, result.Status, result.ToString());
            return FixMath.F64.RoundToInt(result.Value.Numeric);
        }

        [Test]
        public void Handle_SurvivesLoadBytecode()
        {
            var (source, _) = Script("fun f []\n    return \"from bytecode\"\nend\n");
            var bytes = new MemoryStream();
            source.SaveBytecode(bytes);

            var (script, _) = Script("fun f []\n    return \"from source\"\nend\n");
            script.Run();
            var f = script.GetFunction("f", 0);
            Assert.AreEqual("from source", Text(script.Call(f)));

            bytes.Position = 0;
            script.LoadBytecode(bytes);

            Assert.AreEqual("from bytecode", Text(script.Call(f)));
        }

        [Test]
        public void Handle_ToANative_FollowsAScriptFunctionThatShadowsIt()
        {
            var (script, _) = Script("fun greet []\n    return \"script\"\nend\n");
            AddNative(script, "greet", 0, _ => WarValue.FromText("native"));
            var greet = script.GetFunction("greet", 0);
            Assert.IsInstanceOf<NativeFunctionDefinition>(greet.Definition, "before compiling only the native exists");

            script.Run();

            Assert.AreEqual("script", Text(script.Call(greet)));
        }

        [Test]
        public void Handle_DoesNotLookUpAgain_WhileTheDefinitionsAreUnchanged()
        {
            var (script, _) = Script("fun f []\nend\n");
            script.Run();
            var f = script.GetFunction("f", 0);
            var definition = f.Definition;
            var version = script.DefinitionsVersion;

            script.Call(f);
            script.Call(f);

            Assert.AreEqual(version, script.DefinitionsVersion, "calls do not change the definitions");
            Assert.AreSame(definition, f.Definition);
        }

        [Test]
        public void DefinitionsVersion_ChangesOnReloadLoadBytecodeAndAddFunction()
        {
            var (script, _) = Script("fun f []\nend\n");
            script.Run();

            var before = script.DefinitionsVersion;
            script.Reload("fun f []\nend\n");
            Assert.AreNotEqual(before, script.DefinitionsVersion, "Reload");

            before = script.DefinitionsVersion;
            var bytes = new MemoryStream();
            script.SaveBytecode(bytes);
            bytes.Position = 0;
            script.LoadBytecode(bytes);
            Assert.AreNotEqual(before, script.DefinitionsVersion, "LoadBytecode");

            before = script.DefinitionsVersion;
            AddNative(script, "n", 0, _ => WarValue.Null);
            Assert.AreNotEqual(before, script.DefinitionsVersion, "AddFunction");
        }

        [Test]
        public void Handle_FollowsAFunctionAnImportDefines()
        {
            var files = new Dictionary<string, string> { ["lib"] = "fun helper []\n    return \"imported\"\nend\n" };
            var output = new List<string>();
            var script = new WarScriptLanguage("main", "import \"lib\"\n",
                path => files.TryGetValue(path, out var s) ? s : null, (s, m) => output.Add(m));
            script.LogUnhandledErrors = false;
            AddNative(script, "helper", 0, _ => WarValue.FromText("native"));
            var helper = script.GetFunction("helper", 0);
            Assert.IsInstanceOf<NativeFunctionDefinition>(helper.Definition);

            Assert.IsTrue(script.Run().IsOk);

            Assert.AreEqual("imported", Text(script.Call(helper)));
        }

        [Test]
        public void StaleHandle_CalledFromANative_RaisesARescuableNameError()
        {
            var (script, output) = Script(@"fun on_event []
end
begin
    fire []
rescue e
    print e
end
");
            FunctionHandle handler = null;
            AddNative(script, "fire", 0, _ => { script.Call(handler); return WarValue.Null; });
            Assert.IsTrue(script.Compile().Success);
            handler = script.GetFunction("on_event", 0);
            script.Reload(@"begin
    fire []
rescue e
    print e
end
");

            Assert.IsTrue(script.Run().IsOk);

            CollectionAssert.AreEqual(new[] { "Function 'on_event' with 0 args is not defined" }, output);
        }

        [Test]
        public void HandleOfAnotherInstance_IsRejected()
        {
            var (first, _) = Script("fun f []\nend\n");
            var (second, _) = Script("fun f []\nend\n");
            first.Run();
            second.Run();

            Assert.Throws<ArgumentException>(() => second.Call(first.GetFunction("f", 0)));
            Assert.Throws<ArgumentNullException>(() => second.Call(null));
            Assert.IsFalse(second.IsHalted, "a host error is not a script fault");
        }
    }
}
