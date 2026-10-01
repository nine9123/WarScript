using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;
using FixMath;

namespace Tests
{
    [TestFixture]
    public class BytecodeCoroutineTests
    {
        // ── Basic yield (same behavior as tree-walk coroutines) ──

        [Test]
        public void BasicYield()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun sequence []
                    print ""step 1""
                    yield
                    print ""step 2""
                    yield
                    print ""step 3""
                end
            ");

            script.StartCoroutine("sequence", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "step 1" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 1", "step 2" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 1", "step 2", "step 3" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldWait()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun delayed []
                    print ""start""
                    yield wait 1
                    print ""after wait""
                end
            ");

            script.StartCoroutine("delayed", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "start" }, output);

            script.TickCoroutines(F64.FromDouble(0.5));
            Assert.AreEqual(new[] { "start" }, output);

            script.TickCoroutines(F64.FromDouble(0.3));
            Assert.AreEqual(new[] { "start" }, output);

            script.TickCoroutines(F64.FromDouble(0.3));
            Assert.AreEqual(new[] { "start", "after wait" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void PreservesVariables()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun sequence []
                    name = ""hero""
                    hp = 100
                    print ""{name}: {hp}""
                    yield
                    hp -= 25
                    print ""{name}: {hp}""
                    yield
                    hp -= 50
                    print ""{name}: {hp}""
                end
            ");

            script.StartCoroutine("sequence", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "hero: 100" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hero: 100", "hero: 75" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hero: 100", "hero: 75", "hero: 25" }, output);
        }

        [Test]
        public void WithArguments()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun greet [name, count]
                    print ""hello {name}""
                    yield
                    print ""goodbye {name} ({count})""
                end
            ");

            script.StartCoroutine("greet", new[]
            {
                WarValue.FromText("world"),
                WarValue.FromNumeric(42)
            });
            Assert.AreEqual(new[] { "hello world" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hello world", "goodbye world (42)" }, output);
        }

        [Test]
        public void LoopingCoroutine()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun pulse []
                    print ""on""
                    yield
                    print ""off""
                end
            ");

            script.StartCoroutine("pulse", System.Array.Empty<WarValue>(), loop: true);
            Assert.AreEqual(new[] { "on" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on", "off" }, output);

            Assert.AreEqual(1, script.ActiveCoroutineCount);
        }

        [Test]
        public void StopById()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun forever []
                    print ""tick""
                    yield
                    print ""tick""
                end
            ");

            var id = script.StartCoroutine("forever", System.Array.Empty<WarValue>(), loop: true);
            Assert.AreEqual(new[] { "tick" }, output);

            script.StopCoroutine(id);
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "tick" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void MultipleCoroutines()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun a []
                    print ""a1""
                    yield
                    print ""a2""
                end
                fun b []
                    print ""b1""
                    yield
                    print ""b2""
                end
            ");

            script.StartCoroutine("a", System.Array.Empty<WarValue>());
            script.StartCoroutine("b", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "a1", "b1" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        // ══════════════════════════════════════════════════════════
        //  New bytecode-only capabilities
        //
        //  These patterns were impossible with the tree-walk coroutine
        //  system because it splits functions at yield points during
        //  parsing — yield could only appear at the top level of the
        //  function body, never inside loops or nested calls.
        // ══════════════════════════════════════════════════════════

        [Test]
        public void YieldInsideForLoop()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun patrol [waypoints]
                    loop wp in waypoints
                        print ""moving to "" + wp
                        yield
                    end
                    print ""patrol done""
                end
            ");

            script.StartCoroutine("patrol", new[]
            {
                WarValue.FromArray(new List<WarValue>
                {
                    WarValue.FromText("A"),
                    WarValue.FromText("B"),
                    WarValue.FromText("C")
                })
            });
            Assert.AreEqual(new[] { "moving to A" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "moving to A", "moving to B" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "moving to A", "moving to B", "moving to C" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "moving to A", "moving to B", "moving to C", "patrol done" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldInsideWhileLoop()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun countdown []
                    i = 5
                    loop i > 0
                        print i
                        i -= 1
                        yield
                    end
                    print ""done""
                end
            ");

            script.StartCoroutine("countdown", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "5" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "5", "4" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "5", "4", "3" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "5", "4", "3", "2" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "5", "4", "3", "2", "1" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "5", "4", "3", "2", "1", "done" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldInsideNestedIfInsideLoop()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun process [items]
                    loop item in items
                        if item > 0
                            print ""positive: "" + item
                        else
                            print ""skipped: "" + item
                        end
                        yield
                    end
                end
            ");

            script.StartCoroutine("process", new[]
            {
                WarValue.FromArray(new List<WarValue>
                {
                    WarValue.FromNumeric(3),
                    WarValue.FromNumeric(-1),
                    WarValue.FromNumeric(7)
                })
            });
            Assert.AreEqual(new[] { "positive: 3" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "positive: 3", "skipped: -1" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "positive: 3", "skipped: -1", "positive: 7" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldInsideNestedFunctionCall()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun do_step [n]
                    print ""step "" + n
                    yield
                    print ""step "" + n + "" done""
                end
                fun main_routine []
                    do_step [1]
                    do_step [2]
                end
            ");

            script.StartCoroutine("main_routine", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "step 1" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 1", "step 1 done", "step 2" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 1", "step 1 done", "step 2", "step 2 done" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void CallerVariablesSurviveYieldInsideCalledFunction()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun helper []
                    yield wait 1
                    return 1
                end
                fun on_run [room]
                    keep = ""x""
                    got = helper []
                    print keep
                    print got
                    print room
                end
            ");

            script.StartCoroutine("on_run", new[] { WarValue.FromText("lobby") });
            Assert.AreEqual(0, output.Count);

            script.TickCoroutines(F64.FromInt(1));
            Assert.AreEqual(new[] { "x", "1", "lobby" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void CallerVariablesSurviveRepeatedYieldsInDeeperCalls()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun inner [n]
                    local_n = n * 10
                    yield
                    yield
                    return local_n
                end
                fun middle [n]
                    before = ""m"" + n
                    v = inner [n]
                    return before + "":"" + v
                end
                fun outer []
                    tag = ""outer""
                    a = middle [1]
                    b = middle [2]
                    print tag + "" "" + a + "" "" + b
                end
            ");

            script.StartCoroutine("outer", System.Array.Empty<WarValue>());
            for (int i = 0; i < 4; i++)
                script.TickCoroutines(F64.FromDouble(0.016));

            Assert.AreEqual(new[] { "outer m1:10 m2:20" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldInsideMethodPreservesCallerAndInstance()
        {
            var (script, output) = TestHelper.Run("test", @"
                class Unit [hp]
                    fun hit [n]
                        yield
                        this :: hp = this :: hp - n
                        return this :: hp
                    end
                end
                fun fight []
                    label = ""unit""
                    u = new Unit [10]
                    left = u :: hit [3]
                    print label + "" "" + left
                end
            ");

            script.StartCoroutine("fight", System.Array.Empty<WarValue>());
            script.TickCoroutines(F64.FromDouble(0.016));

            Assert.AreEqual(new[] { "unit 7" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldInsideCalledFunctionDoesNotLeakIntoHostCalls()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun helper []
                    yield
                    return 1
                end
                fun co []
                    keep = ""co""
                    helper []
                    print keep
                end
                fun probe []
                    print ""probe {keep}""
                end
            ");

            script.StartCoroutine("co", System.Array.Empty<WarValue>());
            // A host call between ticks must not see the suspended coroutine's scopes.
            script.Call(script.GetFunction("probe", 0));
            script.TickCoroutines(F64.FromDouble(0.016));
            script.Call(script.GetFunction("probe", 0));

            Assert.AreEqual(new[] { "probe null", "co", "probe null" }, output);
        }

        [Test]
        public void YieldUntilCondition()
        {
            var (script, output) = TestHelper.Run("test", @"
                counter = 0
                fun increment []
                    counter += 1
                end
                fun wait_for_five []
                    print ""waiting""
                    yield until counter >= 5
                    print ""reached""
                end
            ");

            script.StartCoroutine("wait_for_five", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "waiting" }, output);

            var increment = script.GetFunction("increment", 0);

            for (int i = 0; i < 4; i++)
            {
                script.Call(increment);
                script.TickCoroutines(F64.FromDouble(0.016));
            }
            Assert.AreEqual(new[] { "waiting" }, output);

            script.Call(increment);
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "waiting", "reached" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void YieldWaitInsideLoop()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun heartbeat []
                    loop i in 0..3
                        print ""beat "" + i
                        yield wait 0.5
                    end
                end
            ");

            script.StartCoroutine("heartbeat", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "beat 0" }, output);

            script.TickCoroutines(F64.FromDouble(0.3));
            Assert.AreEqual(new[] { "beat 0" }, output);

            script.TickCoroutines(F64.FromDouble(0.3));
            Assert.AreEqual(new[] { "beat 0", "beat 1" }, output);

            script.TickCoroutines(F64.FromDouble(0.5));
            Assert.AreEqual(new[] { "beat 0", "beat 1", "beat 2" }, output);

            script.TickCoroutines(F64.FromDouble(0.5));
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void LoopingCoroutineWithYieldInsideLoop()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun blink []
                    loop i in 0..2
                        print ""on""
                        yield
                        print ""off""
                        yield
                    end
                end
            ");

            script.StartCoroutine("blink", System.Array.Empty<WarValue>(), loop: true);
            Assert.AreEqual(new[] { "on" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on" }, output);

            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on", "off" }, output);

            // This tick completes the inner loop (i=2, exits) — no new output
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on", "off" }, output);

            // loop=true restarts the function on the next tick
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "on", "off", "on", "off", "on" }, output);

            Assert.AreEqual(1, script.ActiveCoroutineCount);
        }

        [Test]
        public void CoroutineReadsMutatedGlobalState()
        {
            var (script, output) = TestHelper.Run("test", @"
                hp = 100
                fun drain []
                    hp -= 10
                end
                fun monitor []
                    loop hp > 50
                        print ""hp: "" + hp
                        yield
                    end
                    print ""low hp!""
                end
            ");

            script.StartCoroutine("monitor", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "hp: 100" }, output);

            var drain = script.GetFunction("drain", 0);

            script.Call(drain); // hp = 90
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hp: 100", "hp: 90" }, output);

            script.Call(drain); // hp = 80
            script.Call(drain); // hp = 70
            script.Call(drain); // hp = 60
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hp: 100", "hp: 90", "hp: 60" }, output);

            script.Call(drain); // hp = 50 — condition fails
            script.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "hp: 100", "hp: 90", "hp: 60", "low hp!" }, output);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }
    }
}
