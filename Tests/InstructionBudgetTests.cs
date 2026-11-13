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

        // ── An exhausted budget stays exhausted (BUDGET-2, Q-11) ──
        // The error is raised again by every further instruction until the
        // next outermost entry, so a rescue or ensure body cannot run and the
        // error ends the entry whatever the script rescues.

        /// <summary>The printed lines, without the log line of the unhandled error.</summary>
        private static List<string> Printed(WarScriptLanguage script, List<string> output)
        {
            var printed = new List<string>(output);
            var unhandled = script.ExceptionContext.LastUnhandled;
            if (unhandled != null) printed.Remove(unhandled.ToString());
            return printed;
        }

        private static string Unhandled(WarScriptLanguage script) =>
            script.ExceptionContext.LastUnhandled?.Value.ToString();

        [Test]
        public void BudgetExceeded_RescueBodyDoesNotRun()
        {
            var (script, output) = TestHelper.Run("test", @"
                caught = false
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                    caught = true
                    print ""caught""
                end
                print caught
            ", setupScope: (s, _) => { s.InstructionBudget = 5000; });

            CollectionAssert.IsEmpty(Printed(script, output));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(script));
        }

        [Test]
        public void BudgetExceeded_EndsTheEntryEvenWhenRescued()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun after []
                    print ""next entry""
                end
                begin
                    loop 1 == 1
                        x = 1
                    end
                rescue e
                end
                print ""still running""
            ", setupScope: (s, _) => { s.InstructionBudget = 2000; });

            CollectionAssert.IsEmpty(Printed(script, output));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(script));

            // The next outermost entry starts with a full budget.
            output.Clear();
            script.Call(script.GetFunction("after", 0));
            CollectionAssert.AreEqual(new[] { "next entry" }, output);
        }

        [Test]
        public void BudgetExceeded_SkipsRescueAndEnsureBodies()
        {
            var (script, output) = TestHelper.Run("test", @"
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
            ", setupScope: (s, _) => { s.InstructionBudget = 3000; });

            CollectionAssert.IsEmpty(Printed(script, output));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(script));
        }

        [Test]
        public void BudgetExceeded_EnsureOnlyBlockDoesNotRun()
        {
            // An ensure body could loop forever too, so it gets no instructions.
            var (script, output) = TestHelper.Run("test", @"
                begin
                    loop 1 == 1
                        x = 1
                    end
                ensure
                    loop 1 == 1
                        print ""cleanup""
                    end
                end
            ", setupScope: (s, _) => { s.InstructionBudget = 2000; });

            CollectionAssert.IsEmpty(Printed(script, output));
            Assert.AreEqual("Instruction budget exceeded", Unhandled(script));
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
    }
}
