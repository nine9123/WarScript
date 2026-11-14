using System;
using System.Collections.Generic;
using System.IO;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// The fault and halt model (v2 plan T2.4, 6.3, 6.4; DEC-12, DEC-13,
    /// PROP-3): an error or host fault that escapes an outermost entry halts
    /// the instance; a halted instance answers every entry with
    /// <c>Halted</c> until <c>ClearFault</c> or <c>Reload</c>.
    /// </summary>
    [TestFixture]
    public class FaultTests
    {
        private const string Source = @"
fun ok []
    print ""ok""
    return 1
end
fun fail []
    x = 1 / 0
end
fun co []
    print ""co""
    yield
    print ""co again""
end
";

        private static (WarScriptLanguage script, List<string> output) Script(string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("f", source, null, (s, m) => output.Add(m));
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
            return (script, output);
        }

        private static void AddNative(WarScriptLanguage script, string name, Func<List<WarValue>, WarValue> body)
        {
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, new List<string>()), body, "", ""));
        }

        // ── Halting ─────────────────────────────────────────────

        [Test]
        public void UnhandledError_HaltsTheInstance_AndIsLoggedOnce()
        {
            var (script, output) = Script(Source);
            Assert.IsTrue(script.Run().IsOk);
            Assert.IsFalse(script.IsHalted);
            Assert.IsNull(script.Fault);

            var result = script.Call(script.GetFunction("fail", 0));

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.IsTrue(script.IsHalted);
            Assert.AreSame(result.Error, script.Fault);
            CollectionAssert.AreEqual(new[] { "[WarScript] f:7:11: DivisionByZero: Division by zero\n    x = 1 / 0\n          ^\n  at fail (f:7:11)" }, output);
        }

        [Test]
        public void HaltedInstance_AnswersEveryEntryWithHalted_AndRunsNothing()
        {
            var (script, output) = Script(Source);
            script.Run();
            var fault = script.Call(script.GetFunction("fail", 0)).Error;
            output.Clear();
            var before = Invariants.Capture(script);

            var run = script.Run();
            var call = script.Call(script.GetFunction("ok", 0));
            var start = script.StartCoroutine("co", null, false, out var id);
            var tick = script.TickCoroutines(F64.One);

            foreach (var result in new[] { run, call, start, tick })
            {
                Assert.AreEqual(ScriptStatus.Halted, result.Status);
                Assert.AreSame(fault, result.Error, "the original fault");
                Assert.IsTrue(result.Value.IsNull);
            }
            Assert.AreEqual(-1, id);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            CollectionAssert.IsEmpty(output, "nothing ran and nothing was logged");
            Assert.IsNull(Invariants.Violation(script, before));
        }

        [Test]
        public void ClearFault_ResumesNormalOperation_KeepingTheGlobals()
        {
            var (script, output) = Script("count = 0\nfun bump []\n    global count\n    count += 1\n    return count\nend\nfun fail []\n    global count\n    count += 10\n    raise \"no\"\nend\n");
            script.Run();
            script.Call(script.GetFunction("fail", 0));
            Assert.IsTrue(script.IsHalted);

            script.ClearFault();

            Assert.IsFalse(script.IsHalted);
            Assert.IsNull(script.Fault);
            var bumped = script.Call(script.GetFunction("bump", 0));
            Assert.IsTrue(bumped.IsOk);
            Assert.AreEqual(F64.FromInt(11), bumped.Value.Numeric, "the state changed before the fault stays");
        }

        [Test]
        public void Reload_ClearsTheFault()
        {
            var (script, output) = Script(Source);
            script.Run();
            script.Call(script.GetFunction("fail", 0));
            Assert.IsTrue(script.IsHalted);

            script.Reload(Source);

            Assert.IsFalse(script.IsHalted);
            Assert.IsNull(script.Fault);
            Assert.IsTrue(script.Call(script.GetFunction("ok", 0)).IsOk);
        }

        [Test]
        public void CompileError_HaltsTheInstance_UntilAReloadThatCompiles()
        {
            var (script, output) = Script("x = (\n");

            var first = script.Run();
            var second = script.Run();

            Assert.AreEqual(ScriptErrorKind.CompileError, first.Error.Kind);
            Assert.IsTrue(script.IsHalted);
            Assert.AreEqual(ScriptStatus.Halted, second.Status);
            Assert.AreSame(first.Error, second.Error);
            Assert.AreEqual(1, output.Count);

            script.Reload("print \"fixed\"");
            Assert.IsTrue(script.Run().IsOk);
            CollectionAssert.Contains(output, "fixed");
        }

        [Test]
        public void LoadBytecode_DoesNotClearTheFault()
        {
            var (compiler, _) = Script(Source);
            var bytes = new MemoryStream();
            compiler.SaveBytecode(bytes);
            var (script, _) = Script("x = (\n");
            script.Run();
            Assert.IsTrue(script.IsHalted);

            bytes.Position = 0;
            script.LoadBytecode(bytes);

            Assert.IsTrue(script.IsHalted, "only ClearFault and Reload clear it (plan 6.4)");
            script.ClearFault();
            Assert.IsTrue(script.Call(script.GetFunction("ok", 0)).IsOk);
        }

        [Test]
        public void HostFault_HaltsTheInstance()
        {
            var (script, output) = Script("fun f []\n    x = 1\nend\n");
            script.Run();
            script.AddBreakpoint(2);
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");

            var result = script.Call(script.GetFunction("f", 0));

            Assert.AreEqual(ScriptStatus.HostFault, result.Status);
            Assert.IsTrue(script.IsHalted);
            Assert.AreSame(result.Error, script.Fault);
            Assert.AreEqual(1, output.Count);
            StringAssert.StartsWith("[WarScript] f:0:0: HostFault: host failed\nSystem.InvalidOperationException: host failed", output[0]);
        }

        [Test]
        public void RefusedEntry_HaltsTheInstance()
        {
            var (script, _) = Script(Source);
            script.Run();

            var start = script.StartCoroutine("missing", null, false, out var id);

            Assert.AreEqual(ScriptStatus.Error, start.Status);
            Assert.AreEqual(ScriptErrorKind.NameError, start.Error.Kind);
            Assert.AreEqual(-1, id);
            Assert.IsTrue(script.IsHalted);
            Assert.AreSame(start.Error, script.Fault);
            Assert.IsFalse(script.ExceptionContext.IsRaised());
        }

        [Test]
        public void ErrorRescuedByTheCallingScript_DoesNotHalt()
        {
            var (script, output) = Script(@"
fun on_event []
    x = 1 / 0
end
begin
    fire []
rescue e
    print ""rescued""
end");
            AddNative(script, "fire", _ =>
            {
                script.Call(script.GetFunction("on_event", 0));
                return WarValue.Null;
            });

            Assert.IsTrue(script.Run().IsOk);

            Assert.IsFalse(script.IsHalted);
            CollectionAssert.AreEqual(new[] { "rescued" }, output);
        }

        private sealed class Widget { }

        [TestCase(false, TestName = "HostFaultInAFormattersCallback_StopsTheScript(formatter returns)")]
        [TestCase(true, TestName = "HostFaultInAFormattersCallback_StopsTheScript(formatter throws a script error)")]
        public void HostFaultInAFormattersCallback_StopsTheScript(bool formatterThrows)
        {
            // A native formatter is host code that may call back (6.5); a host
            // fault in that nested entry stops the formatting VM as well, like
            // one in a native's callback (6.3), and is not rescuable.
            var (script, output) = Script(@"fun on_format []
    x = 1
end
w = widget []
begin
    print ""w="" + w
rescue e
    print ""rescued""
end
print ""after""");
            AddNative(script, "widget", _ => WarValue.FromNativeObject(new Widget()));
            script.RegisterNativeFormatter<Widget>(_ =>
            {
                script.Call(script.GetFunction("on_format", 0));
                if (formatterThrows)
                    throw new WarScript.Exception.ScriptErrorException(ScriptErrorKind.NativeError, "cannot format");
                return "widget";
            });
            script.AddBreakpoint(2); // in on_format
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");
            var before = Invariants.Capture(script);

            var result = script.Run();

            Assert.IsNull(Invariants.Violation(script, before));
            Assert.AreEqual(ScriptStatus.HostFault, result.Status);
            Assert.IsInstanceOf<InvalidOperationException>(result.Error.HostException);
            Assert.IsTrue(script.IsHalted);
            CollectionAssert.DoesNotContain(output, "rescued");
            CollectionAssert.DoesNotContain(output, "after");
            CollectionAssert.DoesNotContain(output, "w=widget");
        }

        // ── Logging and the Faulted event ───────────────────────

        [Test]
        public void LogUnhandledErrors_False_LogsNothing()
        {
            var (script, output) = Script("print \"before\"\nraise \"no\"\n");
            script.LogUnhandledErrors = false;

            var result = script.Run();

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.IsTrue(script.IsHalted);
            CollectionAssert.AreEqual(new[] { "before" }, output);
        }

        [Test]
        public void Faulted_IsInvokedOnce_AfterTheCoroutinesAreClearedAndTheErrorLogged()
        {
            var (script, output) = Script(Source);
            script.Run();
            script.StartCoroutine("co", null, false, out _);
            output.Clear();
            var calls = new List<string>();
            script.Faulted += (instance, error) =>
            {
                Assert.AreSame(script, instance);
                Assert.IsTrue(instance.IsHalted);
                Assert.AreSame(error, instance.Fault);
                Assert.AreEqual(0, instance.ActiveCoroutineCount);
                Assert.AreEqual(1, output.Count, "logged before the event");
                calls.Add(error.Message);
                // Entries made from the handler answer Halted.
                Assert.AreEqual(ScriptStatus.Halted, instance.Call(instance.GetFunction("ok", 0)).Status);
            };

            var result = script.Call(script.GetFunction("fail", 0));
            script.Call(script.GetFunction("ok", 0)); // Halted: no second event

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            CollectionAssert.AreEqual(new[] { "Division by zero" }, calls);
            CollectionAssert.DoesNotContain(output, "ok");
        }

        // ── Coroutines (PROP-3) ─────────────────────────────────

        [Test]
        public void CoroutineError_StopsTheTick_AndHalts()
        {
            var (script, output) = Script(@"
fun worker [name]
    yield
    print name
end
fun bad []
    yield
    raise ""bad""
end");
            script.Run();
            script.StartCoroutine("worker", new[] { WarValue.FromText("oldest") }, false, out _);
            script.StartCoroutine("bad", null, false, out _);
            script.StartCoroutine("worker", new[] { WarValue.FromText("newest") }, false, out _);

            var tick = script.TickCoroutines(F64.One);

            // Newest first (PROP-24): "newest" runs, "bad" fails, "oldest" does not run.
            Assert.AreEqual(ScriptStatus.Error, tick.Status);
            Assert.AreEqual("bad", tick.Error.Message);
            Assert.IsTrue(script.IsHalted);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            CollectionAssert.AreEqual(new[] { "newest", "[WarScript] " + tick.Error }, output);
        }

        [Test]
        public void CoroutinesStoppedDuringATick_AreNotResumed()
        {
            var (script, output) = Script(@"
fun worker [name]
    loop true
        yield
        print name
    end
end
fun stopper []
    yield
    stop_all_coroutines []
    print ""stopped""
end");
            script.Run();
            script.StartCoroutine("worker", new[] { WarValue.FromText("a") }, false, out _);
            script.StartCoroutine("worker", new[] { WarValue.FromText("b") }, false, out _);
            script.StartCoroutine("stopper", null, false, out _);

            ScriptResult tick = default;
            Assert.DoesNotThrow(() => tick = script.TickCoroutines(F64.One));

            Assert.IsTrue(tick.IsOk, tick.ToString());
            Assert.AreEqual(F64.Zero, tick.Value.Numeric);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            CollectionAssert.AreEqual(new[] { "stopped" }, output);
        }

        [Test]
        public void CoroutineStoppedByAnotherDuringATick_IsNotResumed_AndTheRestRun()
        {
            var (script, output) = Script(@"
fun worker [name]
    loop true
        yield
        print name
    end
end
fun stopper [victim]
    yield
    stop_coroutine [victim]
    print ""stopped""
end");
            script.Run();
            script.StartCoroutine("worker", new[] { WarValue.FromText("a") }, false, out _);
            script.StartCoroutine("worker", new[] { WarValue.FromText("victim") }, false, out var victim);
            script.StartCoroutine("stopper", new[] { WarValue.FromNumeric(F64.FromInt(victim)) }, false, out _);

            var tick = script.TickCoroutines(F64.One);

            Assert.IsTrue(tick.IsOk, tick.ToString());
            CollectionAssert.AreEqual(new[] { "stopped", "a" }, output);
            Assert.AreEqual(F64.One, tick.Value.Numeric, "only 'a' is left");
        }

        [Test]
        public void CoroutineStartedByAScript_ErrorStaysRaisedForTheScript()
        {
            var (script, output) = Script(@"
fun bad []
    raise ""first run failed""
end
begin
    start_coroutine [""bad"", {}]
    print ""not reached""
rescue e
    print ""rescued: "" + e
end");

            var result = script.Run();

            Assert.IsTrue(result.IsOk, result.ToString());
            Assert.IsFalse(script.IsHalted);
            // The coroutine ended; like one that completes on its first run,
            // the next tick drops it.
            Assert.IsTrue(script.TickCoroutines(F64.One).IsOk);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
            CollectionAssert.AreEqual(new[] { "rescued: first run failed" }, output);
        }
    }
}
