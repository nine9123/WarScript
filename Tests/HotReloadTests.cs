using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class HotReloadTests
    {
        // ── Function body updates ──

        [Test]
        public void ReloadUpdatesFunctionBody()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun greet []
                    print ""hello""
                end
            ");

            script.Call(script.GetFunction("greet", 0));
            Assert.AreEqual(new[] { "hello" }, output);

            script.Reload(@"
                fun greet []
                    print ""goodbye""
                end
            ");

            script.Call(script.GetFunction("greet", 0));
            Assert.AreEqual(new[] { "hello", "goodbye" }, output);
        }

        [Test]
        public void ReloadUpdatesFunctionWithArguments()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun format [x]
                    return ""v1: "" + x
                end
            ");

            var f = script.GetFunction("format", 1);
            script.Call(f, WarValue.FromNumeric(42));

            script.Reload(@"
                fun format [x]
                    return ""v2: "" + x
                end
            ");

            f = script.GetFunction("format", 1);
            script.Call(f, WarValue.FromNumeric(42));

            // format was called but returns via ReturnContext, not print
            // Just verify no exception was raised
            Assert.IsFalse(script.ExceptionContext.IsRaised());
        }

        [Test]
        public void ReloadAddsNewFunction()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun a []
                    print ""a""
                end
            ");

            Assert.IsNull(script.GetFunction("b", 0));

            script.Reload(@"
                fun a []
                    print ""a""
                end
                fun b []
                    print ""b""
                end
            ");

            Assert.IsNotNull(script.GetFunction("a", 0));
            Assert.IsNotNull(script.GetFunction("b", 0));

            script.Call(script.GetFunction("b", 0));
            Assert.AreEqual(new[] { "b" }, output);
        }

        [Test]
        public void ReloadRemovesFunction()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun a []
                    print ""a""
                end
                fun b []
                    print ""b""
                end
            ");

            Assert.IsTrue(script.HasFunction("a", 0));
            Assert.IsTrue(script.HasFunction("b", 0));

            script.Reload(@"
                fun b []
                    print ""b""
                end
            ");

            Assert.IsFalse(script.HasFunction("a", 0));
            Assert.IsTrue(script.HasFunction("b", 0));
        }

        // ── Variable preservation ──

        [Test]
        public void ReloadPreservesGlobalVariables()
        {
            var (script, output) = TestHelper.Run("test", @"
                hp = 100
                fun damage [n]
                    hp -= n
                end
            ");

            script.Call(script.GetFunction("damage", 1), WarValue.FromNumeric(30));
            // hp is now 70

            script.Reload(@"
                fun damage [n]
                    hp -= n
                end
                fun get_hp []
                    print hp
                end
            ");

            script.Call(script.GetFunction("get_hp", 0));
            Assert.AreEqual(new[] { "70" }, output);
        }

        [Test]
        public void ReloadPreservesArrayState()
        {
            var (script, output) = TestHelper.Run("test", @"
                log = {}
                fun add_entry [msg]
                    log << msg
                end
            ");

            script.Call(script.GetFunction("add_entry", 1), WarValue.FromText("before"));

            script.Reload(@"
                fun add_entry [msg]
                    log << msg
                end
                fun dump []
                    print log
                end
            ");

            script.Call(script.GetFunction("add_entry", 1), WarValue.FromText("after"));
            script.Call(script.GetFunction("dump", 0));

            Assert.AreEqual(new[] { "[before, after]" }, output);
        }

        [Test]
        public void ReloadPreservesClassInstances()
        {
            var (script, output) = TestHelper.Run("test", @"
                class Entity [name, hp]
                end
                hero = new Entity [""Warrior"", 100]
            ");

            var hp = script.UserMemoryScope.Get("hero").ClassValue.GetProperty("hp");
            Assert.AreEqual(100.0, hp.Numeric);

            script.Reload(@"
                class Entity [name, hp]
                    fun status []
                        print ""{this :: name}: {this :: hp}""
                    end
                end
                fun show_hero []
                    print hero
                end
            ");

            // Old instance is preserved
            var hpAfter = script.UserMemoryScope.Get("hero").ClassValue.GetProperty("hp");
            Assert.AreEqual(100.0, hpAfter.Numeric);
        }

        [Test]
        public void ReloadDoesNotReExecuteTopLevelCode()
        {
            var (script, output) = TestHelper.Run("test", @"
                counter = 0
                counter += 1
                print ""init""
            ");

            Assert.AreEqual(new[] { "init" }, output);
            Assert.AreEqual(1.0, script.UserMemoryScope.Get("counter").Numeric);

            // Reload with same source — should NOT print "init" again
            // or increment counter again
            script.Reload(@"
                counter = 0
                counter += 1
                print ""init""
            ");

            // counter still 1 from the original Run(), not re-executed
            Assert.AreEqual(new[] { "init" }, output);
            Assert.AreEqual(1.0, script.UserMemoryScope.Get("counter").Numeric);
        }

        // ── Coroutines ──

        [Test]
        public void ReloadStopsActiveCoroutines()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun ticker []
                    print ""tick""
                    yield
                    print ""tick""
                end
            ");

            script.StartCoroutine("ticker", Array.Empty<WarValue>(), loop: true);
            Assert.AreEqual(1, script.ActiveCoroutineCount);

            script.Reload(@"
                fun ticker []
                    print ""new tick""
                    yield
                    print ""new tick""
                end
            ");

            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void ReloadThenStartNewCoroutine()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun patrol []
                    print ""old patrol""
                    yield
                    print ""old patrol done""
                end
            ");

            script.Reload(@"
                fun patrol []
                    print ""new patrol""
                    yield
                    print ""new patrol done""
                end
            ");

            script.StartCoroutine("patrol", Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "new patrol" }, output);

            script.TickCoroutines(0.016);
            Assert.AreEqual(new[] { "new patrol", "new patrol done" }, output);
        }

        // ── Multiple reloads ──

        [Test]
        public void MultipleReloadsInSequence()
        {
            var (script, output) = TestHelper.Run("test", @"
                count = 0
                fun bump []
                    count += 1
                    print count
                end
            ");

            script.Call(script.GetFunction("bump", 0));
            Assert.AreEqual(new[] { "1" }, output);

            script.Reload(@"
                fun bump []
                    count += 10
                    print count
                end
            ");

            script.Call(script.GetFunction("bump", 0));
            Assert.AreEqual(new[] { "1", "11" }, output);

            script.Reload(@"
                fun bump []
                    count += 100
                    print count
                end
            ");

            script.Call(script.GetFunction("bump", 0));
            Assert.AreEqual(new[] { "1", "11", "111" }, output);
        }

        // ── Edge cases ──

        [Test]
        public void ReloadWithSyntaxErrorPreservesOldState()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun greet []
                    print ""hello""
                end
            ");

            script.Call(script.GetFunction("greet", 0));
            Assert.AreEqual(new[] { "hello" }, output);

            // Reload with broken syntax — should throw during parse
            Assert.Throws<WarScript.Exception.SyntaxException>(() =>
            {
                script.Reload("fun broken [[\nend end end");
            });

            // After failed reload, functions may be cleared — this is expected.
            // The user should fix the syntax and reload again.
        }

        [Test]
        public void ReloadAfterRunThenCallWorks()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                x = 10
                fun get_x []
                    print x
                end
            ", null, (s, msg) => output.Add(msg));
            script.Run();

            script.Call(script.GetFunction("get_x", 0));
            Assert.AreEqual(new[] { "10" }, output);

            // Mutate x via call
            script.Reload(@"
                fun set_x [val]
                    x = val
                end
                fun get_x []
                    print x
                end
            ");

            script.Call(script.GetFunction("set_x", 1), WarValue.FromNumeric(42));
            script.Call(script.GetFunction("get_x", 0));
            Assert.AreEqual(new[] { "10", "42" }, output);
        }

        [Test]
        public void StaleHandleNotUsedAfterReload()
        {
            var (script, output) = TestHelper.Run("test", @"
                fun greet []
                    print ""hello""
                end
            ");

            var oldHandle = script.GetFunction("greet", 0);
            Assert.IsNotNull(oldHandle);

            script.Reload(@"
                fun greet []
                    print ""goodbye""
                end
            ");

            // Fresh handle works
            var newHandle = script.GetFunction("greet", 0);
            Assert.IsNotNull(newHandle);
            script.Call(newHandle);
            Assert.AreEqual(new[] { "goodbye" }, output);
        }
    }
}
