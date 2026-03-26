using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class SourceMapDebuggerTests
    {
        // ── Breakpoints ──

        [Test]
        public void BreakpointFiresAtCorrectLine()
        {
            var hitLines = new List<int>();
            var (script, output) = TestHelper.Run("test", @"
x = 1
x = 2
x = 3
print x
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3);
                s.DebugHook = ctx =>
                {
                    hitLines.Add(ctx.Line);
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.AreEqual(1, hitLines.Count);
            Assert.AreEqual(3, hitLines[0]);
        }

        [Test]
        public void MultipleBreakpoints()
        {
            var hitLines = new List<int>();
            var (script, output) = TestHelper.Run("test", @"
x = 1
x = 2
x = 3
x = 4
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(2);
                s.AddBreakpoint(4);
                s.DebugHook = ctx =>
                {
                    hitLines.Add(ctx.Line);
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.AreEqual(2, hitLines.Count);
            Assert.Contains(2, hitLines);
            Assert.Contains(4, hitLines);
        }

        [Test]
        public void RemoveBreakpointStopsFiring()
        {
            var hitCount = 0;
            var script = new WarScriptLanguage("test", @"
x = 1
x = 2
x = 3
            ", null, null);
            script.AddBreakpoint(2);
            script.DebugHook = ctx =>
            {
                hitCount++;
                ctx.Action = StepMode.Continue;
            };
            script.Run();
            Assert.AreEqual(1, hitCount);

            // Remove breakpoint and run again
            hitCount = 0;
            script.RemoveBreakpoint(2);
            script.Reload(@"
x = 1
x = 2
x = 3
            ");
            script.Run();
            Assert.AreEqual(0, hitCount);
        }

        [Test]
        public void ClearBreakpointsRemovesAll()
        {
            var script = new WarScriptLanguage("test", "x = 1", null, null);
            script.AddBreakpoint(1);
            script.AddBreakpoint(2);
            script.AddBreakpoint(3);
            Assert.AreEqual(3, script.Breakpoints.Count);

            script.ClearBreakpoints();
            Assert.AreEqual(0, script.Breakpoints.Count);
        }

        // ── Step modes ──

        [Test]
        public void StepIntoWalksEveryLine()
        {
            var steppedLines = new List<int>();
            var (script, output) = TestHelper.Run("test", @"
x = 1
y = 2
z = x + y
print z
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(1);
                s.DebugHook = ctx =>
                {
                    steppedLines.Add(ctx.Line);
                    ctx.Action = StepMode.StepInto;
                };
            });

            Assert.IsTrue(steppedLines.Count >= 4);
            Assert.AreEqual(1, steppedLines[0]);
        }

        [Test]
        public void StepOverSkipsFunctionBody()
        {
            var steppedEntries = new List<(string fn, int line)>();
            var (script, output) = TestHelper.Run("test", @"
fun helper []
    x = 1
    x = 2
end
helper []
print ""done""
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(5);
                s.DebugHook = ctx =>
                {
                    steppedEntries.Add((ctx.FunctionName, ctx.Line));
                    ctx.Action = StepMode.StepOver;
                };
            });

            // Should NOT have entries inside "helper"
            var insideHelper = steppedEntries.Any(e => e.fn == "helper");
            Assert.IsFalse(insideHelper);
            Assert.IsTrue(steppedEntries.Count >= 2);
        }

        [Test]
        public void StepIntoEntersFunctionBody()
        {
            var steppedFunctions = new List<string>();
            var (script, output) = TestHelper.Run("test", @"
fun helper []
    x = 1
end
helper []
print ""done""
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(4);
                s.DebugHook = ctx =>
                {
                    steppedFunctions.Add(ctx.FunctionName);
                    ctx.Action = StepMode.StepInto;
                };
            });

            Assert.IsTrue(steppedFunctions.Contains("helper"));
        }

        // ── DebugContext contents ──

        [Test]
        public void LocalVariablesVisibleInCallback()
        {
            Dictionary<string, WarValue> capturedLocals = null;
            var (script, output) = TestHelper.Run("test", @"
fun add [a, b]
    result = a + b
    return result
end
print add [10, 20]
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3);
                s.DebugHook = ctx =>
                {
                    capturedLocals = new Dictionary<string, WarValue>(ctx.Locals);
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.IsNotNull(capturedLocals);
            Assert.IsTrue(capturedLocals.ContainsKey("a"));
            Assert.AreEqual(10.0, capturedLocals["a"].Numeric);
            Assert.IsTrue(capturedLocals.ContainsKey("b"));
            Assert.AreEqual(20.0, capturedLocals["b"].Numeric);
        }

        [Test]
        public void HiddenLocalsExcludedFromLocals()
        {
            Dictionary<string, WarValue> capturedLocals = null;
            var (script, output) = TestHelper.Run("test", @"
fun test []
    loop i in 0..5
        x = i
    end
end
test []
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3);
                s.DebugHook = ctx =>
                {
                    capturedLocals = new Dictionary<string, WarValue>(ctx.Locals);
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.IsNotNull(capturedLocals);
            // i should be visible, $limit and $step should be hidden
            Assert.IsTrue(capturedLocals.ContainsKey("i"));
            Assert.IsFalse(capturedLocals.Keys.Any(k => k.StartsWith("$")));
        }

        [Test]
        public void CallStackShowsFullChain()
        {
            List<DebugContext.StackEntry> capturedStack = null;
            var (script, output) = TestHelper.Run("test", @"
fun inner []
    print ""hi""
end
fun outer []
    inner []
end
outer []
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(2);
                s.DebugHook = ctx =>
                {
                    capturedStack = new List<DebugContext.StackEntry>(ctx.CallStack);
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.IsNotNull(capturedStack);
            Assert.IsTrue(capturedStack.Count >= 3);
            // Deepest entry should be "inner"
            Assert.AreEqual("inner", capturedStack[^1].FunctionName);
        }

        [Test]
        public void FunctionNameCorrectInCallback()
        {
            string capturedName = null;
            var (script, output) = TestHelper.Run("test", @"
fun my_func []
    x = 42
end
my_func []
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(2);
                s.DebugHook = ctx =>
                {
                    capturedName = ctx.FunctionName;
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.AreEqual("my_func", capturedName);
        }

        [Test]
        public void GetGlobalReadsScriptState()
        {
            WarValue capturedHp = WarValue.Null;
            var (script, output) = TestHelper.Run("test", @"
hp = 100
hp -= 25
print hp
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3);
                s.DebugHook = ctx =>
                {
                    capturedHp = ctx.GetGlobal("hp");
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.IsTrue(capturedHp.IsNumeric);
            Assert.AreEqual(75.0, capturedHp.Numeric);
        }

        [Test]
        public void ScriptNameAvailableInContext()
        {
            string capturedScriptName = null;
            var (script, output) = TestHelper.Run("patrol.ws", @"
x = 1
print x
            ", setupScope: (s, _) =>
            {
                s.AddBreakpoint(1);
                s.DebugHook = ctx =>
                {
                    capturedScriptName = ctx.ScriptName;
                    ctx.Action = StepMode.Continue;
                };
            });

            Assert.AreEqual("patrol.ws", capturedScriptName);
        }

        // ── Zero overhead ──

        [Test]
        public void NullHookHasNoEffect()
        {
            // Should complete without any issue — no debug overhead
            var (_, output) = TestHelper.Run("test", @"
                x = 0
                loop i in 0..1000
                    x += 1
                end
                assert x == 1000
                print ""ok""
            ");
            Assert.AreEqual(new[] { "ok" }, output);
        }

        // ── Breakpoints on Call() path ──

        [Test]
        public void BreakpointFiresDuringCall()
        {
            var hitLines = new List<int>();
            var (script, output) = TestHelper.Run("test", @"
fun tick []
    x = 1
    x = 2
end
            ");

            script.AddBreakpoint(3);
            script.DebugHook = ctx =>
            {
                hitLines.Add(ctx.Line);
                ctx.Action = StepMode.Continue;
            };

            var tick = script.GetFunction("tick", 0);
            script.Call(tick);

            Assert.AreEqual(1, hitLines.Count);
            Assert.AreEqual(3, hitLines[0]);
        }
    }
}
