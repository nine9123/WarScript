using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class MemoryBudgetTests
    {
        // ── Budget stops runaway allocations ──

        [Test]
        public void InfiniteArrayAppendStopped()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                arr = {}
                loop 1 == 1
                    arr << ""data""
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 10000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        [Test]
        public void StringConcatBombStopped()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                s = ""x""
                loop 1 == 1
                    s = s + s
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 50000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        [Test]
        public void StringRepeatBombStopped()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                s = ""x"" * 100000
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 10000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        [Test]
        public void ClassCreationBombStopped()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                class Big [a, b, c, d, e, f, g, h]
                end
                loop 1 == 1
                    x = new Big [1,2,3,4,5,6,7,8]
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 50000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        // ── Normal scripts unaffected ──

        [Test]
        public void NormalScriptCompletesWithinBudget()
        {
            var (_, output) = TestHelper.Run("test", @"
                arr = {}
                loop i in 0..10
                    arr << i
                end
                print arr
            ", setupScope: (s, _) =>
            {
                s.MemoryBudget = 100000;
            });

            Assert.AreEqual(1, output.Count);
            Assert.IsTrue(output[0].StartsWith("[0,"));
        }

        [Test]
        public void ZeroBudgetMeansUnlimited()
        {
            var (_, output) = TestHelper.Run("test", @"
                arr = {}
                loop i in 0..1000
                    arr << i
                end
                assert arr{999} == 999
                print ""ok""
            ", setupScope: (s, _) =>
            {
                s.MemoryBudget = 0;
            });

            Assert.AreEqual(new[] { "ok" }, output);
        }

        [Test]
        public void DefaultBudgetIsUnlimited()
        {
            var script = new WarScriptLanguage("test", "x = 1", null, null);
            Assert.AreEqual(0, script.MemoryBudget);
        }

        // ── Catchable via begin/rescue ──

        [Test]
        public void MemoryExceededIsCatchable()
        {
            var (_, output) = TestHelper.Run("test", @"
                caught = false
                begin
                    s = ""x""
                    loop 1 == 1
                        s = s + s
                    end
                rescue e
                    caught = true
                end
                print caught
            ", setupScope: (s, _) =>
            {
                s.InstructionBudget = 1000000;
                s.MemoryBudget = 10000;
            });

            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void MemoryExceededMessageAccessible()
        {
            var (_, output) = TestHelper.Run("test", @"
                msg = null
                begin
                    arr = {}
                    loop 1 == 1
                        arr << ""data""
                    end
                rescue e
                    msg = e
                end
                print msg
            ", setupScope: (s, _) =>
            {
                s.InstructionBudget = 1000000;
                s.MemoryBudget = 10000;
            });

            Assert.AreEqual(new[] { "Memory budget exceeded" }, output);
        }

        [Test]
        public void ScriptContinuesAfterCaughtMemoryExceeded()
        {
            var (_, output) = TestHelper.Run("test", @"
                begin
                    s = ""x""
                    loop 1 == 1
                        s = s + s
                    end
                rescue e
                    print ""caught""
                end
                print ""still running""
            ", setupScope: (s, _) =>
            {
                s.InstructionBudget = 1000000;
                s.MemoryBudget = 20000;
            });

            Assert.AreEqual(new[] { "caught", "still running" }, output);
        }

        // ── Call() path ──

        [Test]
        public void MemoryBudgetWorksOnCallPath()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun grow []
                    arr = {}
                    loop 1 == 1
                        arr << ""data""
                    end
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 10000;
            script.Run();

            script.Call(script.GetFunction("grow", 0));
            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        [Test]
        public void MemoryBudgetResetsPerCall()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun make_stuff []
                    arr = {}
                    loop i in 0..10
                        arr << ""item""
                    end
                end
            ", null, (s, msg) => output.Add(msg));
            script.MemoryBudget = 100000;
            script.Run();

            var func = script.GetFunction("make_stuff", 0);
            for (int i = 0; i < 50; i++)
                script.Call(func);

            Assert.IsFalse(output.Exists(o => o.Contains("Memory budget exceeded")));
        }

        // ── Budget can be changed ──

        [Test]
        public void BudgetCanBeChangedAtRuntime()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                fun grow []
                    arr = {}
                    loop i in 0..100
                        arr << ""item""
                    end
                    print ""done""
                end
            ", null, (s, msg) => output.Add(msg));
            script.MemoryBudget = 1000000;
            script.Run();

            var func = script.GetFunction("grow", 0);

            // Tight budget — should fail
            script.MemoryBudget = 100;
            output.Clear();
            script.Call(func);
            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));

            // Generous budget — should succeed
            script.MemoryBudget = 1000000;
            output.Clear();
            script.Call(func);
            Assert.AreEqual(new[] { "done" }, output);
        }

        // ── Array concat ──

        [Test]
        public void ArrayConcatTracked()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                a = {}
                loop 1 == 1
                    a = a + {1, 2, 3, 4, 5}
                end
            ", null, (s, msg) => output.Add(msg));
            script.InstructionBudget = 1000000;
            script.MemoryBudget = 10000;
            script.Run();

            Assert.IsTrue(output.Exists(o => o.Contains("Memory budget exceeded")));
        }
    }
}
