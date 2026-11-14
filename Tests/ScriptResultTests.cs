using System;
using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Result types (v2 plan T2.3, 6.1, 6.3; API-1, API-2): <c>Run</c>,
    /// <c>Call</c>, <c>StartCoroutine</c> and <c>TickCoroutines</c> return a
    /// <see cref="ScriptResult"/> holding the return value or the error that
    /// escaped the entry. Halting (T2.4) is in <see cref="FaultTests"/>; error
    /// kinds everywhere and call stacks are T2.5.
    /// </summary>
    [TestFixture]
    public class ScriptResultTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("res", source, null, (s, m) => output.Add(m));
            return (script, output);
        }

        private static void AddNative(WarScriptLanguage script, string name, Func<List<WarValue>, WarValue> body)
        {
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, new List<string>()), body, "", ""));
        }

        // ── Ok ──────────────────────────────────────────────────

        [Test]
        public void Run_Completed_IsOkWithANullValue()
        {
            var (script, output) = Script("print \"hi\"");

            var result = script.Run();

            Assert.AreEqual(ScriptStatus.Ok, result.Status);
            Assert.IsTrue(result.IsOk);
            Assert.IsTrue(result.Value.IsNull);
            Assert.IsNull(result.Error);
            CollectionAssert.AreEqual(new[] { "hi" }, output);
        }

        [Test]
        public void Call_ReturnsTheFunctionsValue()
        {
            var (script, _) = Script("fun add [a, b]\n    return a + b\nend\nfun nothing []\nend\n");
            script.Run();

            var sum = script.Call(script.GetFunction("add", 2), WarValue.FromNumeric(F64.FromInt(3)), WarValue.FromNumeric(F64.FromInt(4)));
            var none = script.Call(script.GetFunction("nothing", 0));

            Assert.IsTrue(sum.IsOk);
            Assert.AreEqual(F64.FromInt(7), sum.Value.Numeric);
            Assert.IsTrue(none.IsOk);
            Assert.IsTrue(none.Value.IsNull);
        }

        [Test]
        public void Call_ErrorRescuedInsideTheFunction_IsOk()
        {
            var (script, _) = Script("fun f []\n    begin\n        x = 1 / 0\n    rescue e\n        return e\n    end\nend\n");
            script.Run();

            var result = script.Call(script.GetFunction("f", 0));

            Assert.IsTrue(result.IsOk);
            Assert.AreEqual("Division by zero", result.Value.TextValue);
        }

        // ── Error ───────────────────────────────────────────────

        [Test]
        public void Call_UnhandledError_IsAnErrorResult_AndIsCleared()
        {
            var (script, output) = Script("fun f []\n    return 1 / 0\nend\n");
            script.Run();

            var result = script.Call(script.GetFunction("f", 0));

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.IsFalse(result.IsOk);
            Assert.IsTrue(result.Value.IsNull);
            Assert.AreEqual("Division by zero", result.Error.Message);
            Assert.AreEqual("Division by zero", result.Error.Value.TextValue);
            Assert.AreEqual("res", result.Error.ScriptName);
            Assert.IsNull(result.Error.HostException);
            Assert.AreEqual(0, result.Error.CallStack.Count, "call stacks come with T2.5");
            Assert.IsFalse(script.ExceptionContext.IsRaised(), "the outermost entry still logs and clears it");
            Assert.AreEqual(1, output.Count);
        }

        [Test]
        public void Run_RaisedValue_IsTheErrorsValue()
        {
            var (script, _) = Script("raise {1, 2}");

            var result = script.Run();

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.AreEqual(ScriptErrorKind.Raised, result.Error.Kind);
            Assert.IsTrue(result.Error.Value.IsArray);
            Assert.AreEqual("[1, 2]", result.Error.Message);
        }

        [Test]
        public void NativeErrors_CarryTheirKind()
        {
            var (script, _) = Script("fun bad_arg []\n    return check []\nend\nfun broken []\n    return crash []\nend\n");
            AddNative(script, "check", _ => throw new ScriptErrorException(ScriptErrorKind.ArgumentError, "bad argument"));
            AddNative(script, "crash", _ => throw new InvalidOperationException("boom"));
            script.Run();

            var argument = script.Call(script.GetFunction("bad_arg", 0));
            script.ClearFault();
            var native = script.Call(script.GetFunction("broken", 0));

            Assert.AreEqual(ScriptErrorKind.ArgumentError, argument.Error.Kind);
            Assert.AreEqual("bad argument", argument.Error.Message);
            Assert.AreEqual(ScriptErrorKind.NativeError, native.Error.Kind);
            Assert.AreEqual("Native function 'crash' failed: boom", native.Error.Message);
        }

        [Test]
        public void NestedCall_ErrorResultGoesToTheNative_AndTheErrorContinuesInTheCaller()
        {
            ScriptResult nested = default;
            var (script, output) = Script(@"
fun on_event []
    x = 1 / 0
end
begin
    fire []
    print ""not reached""
rescue e
    print ""rescued: "" + e
end");
            AddNative(script, "fire", _ =>
            {
                nested = script.Call(script.GetFunction("on_event", 0));
                return WarValue.Null;
            });
            var before = Invariants.Capture(script);

            var outer = script.Run();

            Assert.AreEqual(ScriptStatus.Error, nested.Status, "the native sees the callback's error (6.5)");
            Assert.AreEqual("Division by zero", nested.Error.Message);
            Assert.IsTrue(outer.IsOk, "rescued in the calling script (PROP-1)");
            CollectionAssert.AreEqual(new[] { "rescued: Division by zero" }, output);
            Assert.IsNull(Invariants.Violation(script, before));
        }

        [Test]
        public void HostFault_IsAHostFaultResult()
        {
            var (script, _) = Script("fun f []\n    x = 1\n    return x\nend\n");
            script.Run();
            script.AddBreakpoint(2);
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");

            var result = script.Call(script.GetFunction("f", 0));

            Assert.AreEqual(ScriptStatus.HostFault, result.Status);
            Assert.AreEqual(ScriptErrorKind.HostFault, result.Error.Kind);
            Assert.AreEqual("host failed", result.Error.Message);
            Assert.IsInstanceOf<InvalidOperationException>(result.Error.HostException);
        }

        // ── Coroutines ──────────────────────────────────────────

        [Test]
        public void StartCoroutine_GivesTheIdAndTheFirstRunsResult()
        {
            var (script, output) = Script("fun co []\n    print \"a\"\n    yield\n    print \"b\"\nend\nfun bad []\n    yield\n    x = 1 / 0\nend\n");
            script.Run();

            var started = script.StartCoroutine("co", null, false, out var id);
            var other = script.StartCoroutine("bad", null, false, out var badId);

            Assert.IsTrue(started.IsOk);
            Assert.Greater(id, 0);
            Assert.IsTrue(other.IsOk);
            Assert.Greater(badId, id);
            Assert.AreEqual(2, script.ActiveCoroutineCount);

            var tick = script.TickCoroutines(F64.Zero);

            Assert.AreEqual(ScriptStatus.Error, tick.Status, "the failing resume's error");
            Assert.AreEqual("Division by zero", tick.Error.Message);
            // The newest coroutine runs first; its error stops the tick and
            // halts the instance, which clears the other one (PROP-3, T2.4).
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            CollectionAssert.DoesNotContain(output, "b");
        }

        [Test]
        public void TickCoroutines_Ok_CountsTheActiveCoroutines()
        {
            var (script, _) = Script("fun co []\n    yield\n    yield\nend\n");
            script.Run();
            script.StartCoroutine("co", null, false, out _);
            script.StartCoroutine("co", null, false, out _);

            var first = script.TickCoroutines(F64.Zero);
            var second = script.TickCoroutines(F64.Zero);

            Assert.IsTrue(first.IsOk);
            Assert.AreEqual(F64.FromInt(2), first.Value.Numeric);
            Assert.IsTrue(second.IsOk);
            Assert.AreEqual(F64.Zero, second.Value.Numeric);
        }

        [Test]
        public void StartCoroutine_FromANative_LeavesAMissingFunctionsErrorRaisedForTheScript()
        {
            var (script, output) = Script(@"
begin
    start_coroutine [""missing"", {}]
rescue e
    print e
end");
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);

            var result = script.Run();

            Assert.IsTrue(result.IsOk);
            CollectionAssert.AreEqual(new[] { "Coroutine function 'missing' with 0 arguments is not defined" }, output);
        }

        // ── Formatting ──────────────────────────────────────────

        [Test]
        public void ScriptError_ToString_NamesScriptLineKindAndMessage()
        {
            var (script, _) = Script("x = (\n");

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.CompileError, result.Error.Kind);
            StringAssert.StartsWith("res:0:0: CompileError: ", result.Error.ToString());
            StringAssert.StartsWith("Error: res:0:0: CompileError: ", result.ToString());
            Assert.AreEqual("Ok", ScriptResult.Ok(WarValue.Null).ToString());
        }
    }
}
