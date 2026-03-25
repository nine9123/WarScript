using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class InstructionBudgetTests
    {
        // ── Budget stops infinite loops ──

        [Test]
        public void InfiniteWhileLoopStoppedByBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                loop 1 == 1
                    x = 1
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));
        }

        [Test]
        public void InfiniteForLoopStoppedByBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                loop i in 0..999999999
                    x = 1
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 500;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));
        }

        [Test]
        public void InfiniteRecursionStoppedByBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun spin []
                    spin []
                end
                spin []
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 5000;
            script.Run();

            // May hit stack overflow (128 frames) before budget runs out
            Assert.IsTrue(output.Exists(o =>
                o.Contains("budget exceeded") || o.Contains("Stack overflow")));
        }

        // ── Budget does not interfere with normal scripts ──

        [Test]
        public void NormalScriptCompletesWithinBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                sum = 0
                loop i in 0..10
                    sum = sum + i
                end
                print sum
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 10000;
            script.Run();

            Assert.AreEqual(new[] { "45" }, output);
        }

        [Test]
        public void ZeroBudgetMeansUnlimited()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                sum = 0
                loop i in 0..1000
                    sum += 1
                end
                print sum
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 0;
            script.Run();

            Assert.AreEqual(new[] { "1000" }, output);
        }

        [Test]
        public void DefaultBudgetIsUnlimited()
        {
            var script = new WarScriptLanguage("test", "x = 1", null, null);
            Assert.AreEqual(0, script.InstructionBudget);
        }

        // ── Budget is catchable via begin/rescue ──

        [Test]
        public void BudgetExceededIsCatchable()
        {
            var (_, output) = TestHelper.Run("test", @"
                caught = false
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                    caught = true
                end
                print caught
            ", setupScope: (script, _) => { script.InstructionBudget = 5000; });

            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void BudgetExceededMessageAccessibleInRescue()
        {
            var (_, output) = TestHelper.Run("test", @"
                msg = null
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                    msg = e
                end
                print msg
            ", setupScope: (script, _) => { script.InstructionBudget = 1000; });

            Assert.AreEqual(new[] { "Instruction budget exceeded" }, output);
        }

        [Test]
        public void ScriptContinuesAfterCaughtBudgetExceeded()
        {
            var (_, output) = TestHelper.Run("test", @"
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                    print ""caught""
                end
                print ""still running""
            ", setupScope: (script, _) => { script.InstructionBudget = 2000; });

            Assert.AreEqual(new[] { "caught", "still running" }, output);
        }

        // ── Budget on Call() path ──

        [Test]
        public void BudgetStopsInfiniteLoopInCalledFunction()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun spin []
                    loop 1 == 1
                        x = 1
                    end
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000;
            script.Run();

            var spin = script.GetFunction("spin", 0);
            Assert.IsNotNull(spin);

            script.Call(spin);

            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));
        }

        [Test]
        public void BudgetResetsPerCallInvocation()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                count = 0
                fun bump []
                    count = count + 1
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 500;
            script.Run();

            var bump = script.GetFunction("bump", 0);
            Assert.IsNotNull(bump);

            // Each call gets a fresh budget of 500 — bump is tiny, never exceeds it
            for (int i = 0; i < 100; i++)
                script.Call(bump);

            Assert.IsFalse(output.Exists(o => o.Contains("budget exceeded")));
        }

        [Test]
        public void NormalCallCompletesWithinBudget()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun sum_to [n]
                    total = 0
                    loop i in 0..n
                        total += i
                    end
                    return total
                end
            ", setupScope: (s, _) => { s.InstructionBudget = 50000; });

            var sumTo = script.GetFunction("sum_to", 1);
            script.Call(sumTo, WarValue.FromNumeric(100));

            Assert.IsFalse(output.Exists(o => o.Contains("budget exceeded")));
        }

        // ── Budget can be changed between calls ──

        [Test]
        public void BudgetCanBeChangedAtRuntime()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun work []
                    sum = 0
                    loop i in 0..100
                        sum += i
                    end
                    print sum
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 100000;
            script.Run();

            var work = script.GetFunction("work", 0);

            // Call with a tight budget — should fail
            script.InstructionBudget = 50;
            output.Clear();
            script.Call(work);
            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));

            // Call with a generous budget — should succeed
            script.InstructionBudget = 100000;
            output.Clear();
            script.Call(work);
            Assert.AreEqual(new[] { "4950" }, output);
        }

        // ── Budget with class operations ──

        [Test]
        public void BudgetStopsInfiniteLoopInMethod()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                class Spinner []
                    fun spin []
                        loop 1 == 1
                            x = 1
                        end
                    end
                end
                s = new Spinner
                s :: spin []
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 2000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));
        }

        [Test]
        public void BudgetWithClassCreationAndMethods()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Counter [val]
                    fun inc []
                        this :: val = this :: val + 1
                    end
                    fun get []
                        return this :: val
                    end
                end
                c = new Counter [0]
                loop i in 0..10
                    c :: inc []
                end
                print c :: get []
            ", setupScope: (script, _) => { script.InstructionBudget = 50000; });

            Assert.AreEqual(new[] { "10" }, output);
        }

        // ── Budget with nested function calls ──

        [Test]
        public void BudgetAccountsForNestedCalls()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun inner []
                    loop 1 == 1
                        x = 1
                    end
                end
                fun outer []
                    inner []
                end
                outer []
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 2000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("budget exceeded")));
        }

        // ── Budget with exception handling ──

        [Test]
        public void BudgetExceededInTryBlockIsRescuable()
        {
            var (_, output) = TestHelper.Run("test", @"
                result = ""none""
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                    result = ""rescued""
                ensure
                    print ""ensured""
                end
                print result
            ", setupScope: (script, _) => { script.InstructionBudget = 3000; });

            Assert.AreEqual(new[] { "ensured", "rescued" }, output);
        }

        [Test]
        public void BudgetExceededEnsureBlockRuns()
        {
            var (_, output) = TestHelper.Run("test", @"
                begin
                    loop 1 == 1
                        x = 1
                    end
                ensure
                    print ""cleanup""
                end
            ", setupScope: (script, _) => { script.InstructionBudget = 2000; });

            // Ensure runs, then unhandled exception prints stack trace
            Assert.IsTrue(output.Contains("cleanup"));
        }
    }
}
