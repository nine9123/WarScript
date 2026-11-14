using System;
using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Host entries and instance budgets (plan T1.5, 6.5): a native that calls
    /// back into its own instance runs the callback on another VM, and every
    /// VM of the instance — callbacks, imports, coroutines started by a
    /// script — draws from the budgets the outermost entry reset (PROP-2,
    /// BUDGET-1). The re-entrancy repros themselves (A.6, CRASH-4) are in
    /// <see cref="ReproTests"/>.
    /// </summary>
    [TestFixture]
    public class ReentrancyTests
    {
        // About 1,000 to 1,500 instructions: one fits in Budget, two do not.
        private const string Burn = @"
n = 0
loop i in 0..100
    n += 1
end";
        private const int Budget = 2000;

        private static void AddNative(WarScriptLanguage script, string name, Func<List<WarValue>, WarValue> body)
        {
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, new List<string>()), body, "", ""));
        }

        /// <summary>Runs <paramref name="source"/> with a native <c>fire</c> that calls <c>on_event[]</c>.</summary>
        private static (WarScriptLanguage script, List<string> output) RunWithCallback(
            string source, Action<WarScriptLanguage> configure)
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", source, null, (w, m) => output.Add(m));
            AddNative(s, "fire", _ =>
            {
                s.Call(s.GetFunction("on_event", 0));
                return WarValue.Null;
            });
            configure(s);
            var before = Invariants.Capture(s);
            s.Run();
            Assert.IsNull(Invariants.Violation(s, before));
            return (s, output);
        }

        private static string Unhandled(WarScriptLanguage s) =>
            s.ExceptionContext.LastUnhandled?.Value.ToString();

        /// <summary>The printed lines, without the log line of the error that halted the instance (T2.4).</summary>
        private static List<string> Printed(WarScriptLanguage s, List<string> output)
        {
            var printed = new List<string>(output);
            if (s.Fault != null) printed.Remove("[WarScript] " + s.Fault);
            return printed;
        }

        // ── Budgets ─────────────────────────────────────────────────────

        [Test]
        public void CallbackInstructions_CountAgainstTheOuterBudget()
        {
            const string source = @"
fun on_event []" + Burn + @"
end
fire []
fire []
print ""done""";
            // Each callback fits in the budget on its own...
            var (one, oneOutput) = RunWithCallback(source.Replace("fire []\nfire []", "fire []"),
                s => s.InstructionBudget = Budget);
            CollectionAssert.AreEqual(new[] { "done" }, oneOutput);
            Assert.IsNull(Unhandled(one));

            // ...but not both: the nested entries share the outer entry's budget.
            var (two, twoOutput) = RunWithCallback(source, s => s.InstructionBudget = Budget);
            CollectionAssert.IsEmpty(Printed(two, twoOutput));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(two));
        }

        [Test]
        public void CallbackAllocations_CountAgainstTheOuterMemoryBudget()
        {
            // A 1,000-character text is estimated at 2,040 bytes.
            const string source = @"
fun on_event []
    t = ""x"" * 1000
end
loop i in 0..COUNT
    fire []
end
print ""done""";
            var (few, fewOutput) = RunWithCallback(source.Replace("COUNT", "2"), s => s.MemoryBudget = 5000);
            CollectionAssert.AreEqual(new[] { "done" }, fewOutput);
            Assert.IsNull(Unhandled(few));

            var (many, manyOutput) = RunWithCallback(source.Replace("COUNT", "3"), s => s.MemoryBudget = 5000);
            CollectionAssert.IsEmpty(Printed(many, manyOutput));
            Assert.AreEqual("Memory budget exceeded", Unhandled(many));
        }

        [Test]
        public void ImportedScript_SharesTheImportersBudget()
        {
            // v1 ran an import on a VM with a fresh budget (BUDGET-1).
            string Resolve(string path) => path == "burn" ? Burn : null;

            var (alone, aloneOutput) = TestHelper.Run("t", @"
import ""burn""
print ""done""", (s, _) => s.InstructionBudget = Budget, Resolve);
            CollectionAssert.AreEqual(new[] { "done" }, aloneOutput);
            Assert.IsNull(Unhandled(alone));

            var (both, bothOutput) = TestHelper.Run("t", @"
import ""burn""" + Burn + @"
print ""done""", (s, _) => s.InstructionBudget = Budget, Resolve);
            CollectionAssert.IsEmpty(Printed(both, bothOutput));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(both));
        }

        [Test]
        public void EveryOutermostEntry_StartsWithAFullBudget()
        {
            var (script, output) = RunWithCallback(@"
fun on_event []" + Burn + @"
end
fun tick []
    fire []
    print ""tick""
end", s => s.InstructionBudget = Budget);

            var tick = script.GetFunction("tick", 0);
            for (var i = 0; i < 3; i++)
                script.Call(tick);
            CollectionAssert.AreEqual(new[] { "tick", "tick", "tick" }, output);
        }

        [Test]
        public void CoroutineResumedByTheHost_StartsWithAFullBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("t", @"
fun work []
    loop k in 0..3" + Burn.Replace("\n", "\n    ") + @"
        print ""step "" + k
        yield
    end
end", null, (w, m) => output.Add(m));
            script.InstructionBudget = Budget;
            script.Run();
            script.StartCoroutine("work", new WarValue[0]);
            while (script.TickCoroutines(F64.One).Value.Numeric > 0) { }

            CollectionAssert.AreEqual(new[] { "step 0", "step 1", "step 2" }, output);
            Assert.IsNull(Unhandled(script));
            Assert.AreEqual(0, script.EntryDepth);
        }

        [Test]
        public void CoroutineStartedByAScript_SharesTheScriptsBudget()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"
fun work []" + Burn + @"
    print ""work done""
end" + Burn + @"
begin_work []
print ""done""", null, (w, m) => output.Add(m));
            AddNative(s, "begin_work", _ =>
            {
                s.StartCoroutine("work", new WarValue[0]);
                return WarValue.Null;
            });
            s.InstructionBudget = Budget;
            var before = Invariants.Capture(s);
            s.Run();

            Assert.IsNull(Invariants.Violation(s, before));
            // The coroutine runs out of budget and ends; its error stays
            // raised for the script that started it (PROP-1, T2.4), so the
            // run ends with it and the instance halts. It is logged once.
            CollectionAssert.DoesNotContain(output, "work done");
            CollectionAssert.DoesNotContain(output, "done");
            Assert.AreEqual(1, output.FindAll(o => o.Contains("Instruction budget exceeded")).Count,
                string.Join(" | ", output));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(s));
            Assert.IsTrue(s.IsHalted);
            Assert.AreEqual(0, s.ActiveCoroutineCount);
        }

        // ── Nesting ─────────────────────────────────────────────────────

        [Test]
        public void EntryPastTheNestingLimit_RunsNothingAndRaisesForTheCaller()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"
fun go []
    deeper []
end
fun on_event []
    print ""callback""
end
deeper []
print ""done""", null, (w, m) => output.Add(m));
            var level = 0;
            string refused = null;
            AddNative(s, "deeper", _ =>
            {
                level++;
                if (level <= WarScriptLanguage.MaxHostNesting)
                {
                    s.Call(s.GetFunction("go", 0));
                }
                else
                {
                    // The outermost entry plus MaxHostNesting nested ones are running.
                    Assert.AreEqual(WarScriptLanguage.MaxHostNesting + 1, s.EntryDepth);
                    s.Call(s.GetFunction("on_event", 0));
                    refused = s.ExceptionContext.IsRaised()
                        ? s.ExceptionContext.Exception.Value.ToString() : "nothing raised";
                }
                return WarValue.Null;
            });
            var before = Invariants.Capture(s);
            s.Run();

            Assert.IsNull(Invariants.Violation(s, before));
            Assert.AreEqual("Host nesting limit exceeded", refused);
            CollectionAssert.DoesNotContain(output, "callback");
            CollectionAssert.DoesNotContain(output, "done");
            Assert.AreEqual("Host nesting limit exceeded", Unhandled(s));
        }

        [Test]
        public void HostFaultInACallback_StopsTheCallingScriptAndHalts()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"fun on_event []
    x = 1
end
fun main []
    begin
        fire []
    rescue e
        print ""rescued""
    end
    print ""after fire""
end
main []", null, (w, m) => output.Add(m));
            var nested = new List<ScriptStatus>();
            AddNative(s, "fire", _ =>
            {
                // A native that ignores the result cannot let the script go on.
                nested.Add(s.Call(s.GetFunction("on_event", 0)).Status);
                return WarValue.Null;
            });
            s.AddBreakpoint(2); // in on_event
            s.DebugHook = ctx => throw new InvalidOperationException("host failed");
            var before = Invariants.Capture(s);
            ScriptResult result = default;
            Assert.DoesNotThrow(() => result = s.Run());

            Assert.IsNull(Invariants.Violation(s, before));
            // The nested entry returns a HostFault result to the native; when
            // the native returns, the calling VM stops too (no rescue), and
            // the outermost entry reports the fault and halts (T2.4).
            CollectionAssert.AreEqual(new[] { ScriptStatus.HostFault }, nested);
            Assert.AreEqual(ScriptStatus.HostFault, result.Status);
            Assert.IsInstanceOf<InvalidOperationException>(result.Error.HostException);
            Assert.AreSame(result.Error.HostException, s.LastHostFault);
            CollectionAssert.DoesNotContain(output, "rescued");
            CollectionAssert.DoesNotContain(output, "after fire");
            Assert.IsTrue(s.IsHalted);
            Assert.AreSame(result.Error, s.Fault);
            Assert.AreEqual(1, output.FindAll(o => o.StartsWith("[WarScript] t:0:0: HostFault: host failed", StringComparison.Ordinal)).Count,
                string.Join(" | ", output));
        }

        [Test]
        public void CoroutineTickedFromItsOwnNative_IsNotResumedAgain()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"
fun work []
    tick []
    print ""first""
    yield
    print ""second""
end", null, (w, m) => output.Add(m));
            AddNative(s, "tick", _ =>
            {
                s.TickCoroutines(F64.One);
                return WarValue.Null;
            });
            s.Run();
            s.StartCoroutine("work", new WarValue[0]);
            while (s.TickCoroutines(F64.One).Value.Numeric > 0) { }

            CollectionAssert.AreEqual(new[] { "first", "second" }, output);
            Assert.IsNull(s.LastHostFault);
            Assert.AreEqual(0, s.EntryDepth);
        }

        [Test]
        public void SecondCallbackAfterAFailedOne_DoesNotRescueTheFirstError()
        {
            var output = new List<string>();
            WarScriptLanguage s = null;
            s = new WarScriptLanguage("t", @"fun fails []
    raise ""first""
end
fun guarded []
    begin
        print ""guarded ran""
    rescue e
        print ""guarded rescued "" + e
    end
end
begin
    fire []
    print ""after fire""
rescue e
    print ""outer rescued "" + e
end", null, (w, m) => output.Add(m));
            AddNative(s, "fire", _ =>
            {
                s.Call(s.GetFunction("fails", 0));
                s.Call(s.GetFunction("guarded", 0));
                return WarValue.Null;
            });
            var before = Invariants.Capture(s);
            s.Run();
            Assert.IsNull(Invariants.Violation(s, before));
            // The pending "first" is not the second callback's to rescue:
            // that callback does not run, and the error continues from the
            // native call site (PROP-1, 6.5).
            CollectionAssert.AreEqual(new[] { "outer rescued first" }, output);
            Assert.AreEqual(0, s.EntryDepth);
        }

        // ── 'this' ──────────────────────────────────────────────────────

        [Test]
        public void Callback_FromAMethod_HasNoThis()
        {
            // T3.5: 'this' is part of the call frame, so a callback a native
            // makes from inside a method does not see the method's instance.
            var (_, output) = RunWithCallback(@"
fun on_event []
    print this
end
class C [x]
    fun go []
        begin
            fire []
        rescue e
            print e
        end
        print @x
    end
end
(new C [7]) :: go []", _ => { });
            CollectionAssert.AreEqual(new[] { "'this' can only be used inside a class", "7" }, output);
        }
    }
}
