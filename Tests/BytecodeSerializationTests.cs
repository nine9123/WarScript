using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;
using FixMath;

namespace Tests
{
    [TestFixture]
    public class BytecodeSerializationTests
    {
        // ── Round-trip: save → load → call ──

        [Test]
        public void RoundTripSimpleFunction()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun greet []
                    print ""hello""
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("greet", 0));
            Assert.AreEqual(new[] { "hello" }, output2);
        }

        [Test]
        public void RoundTripFunctionWithArgs()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun add [a, b]
                    print a + b
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("add", 2),
                WarValue.FromNumeric(10), WarValue.FromNumeric(20));
            Assert.AreEqual(new[] { "30" }, output2);
        }

        [Test]
        public void RoundTripPreservesAllConstantTypes()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun test_constants []
                    print 3.14
                    print ""pi""
                    print true
                    print null
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("test_constants", 0));
            Assert.AreEqual(new[] { "3.14", "pi", "True", "null" }, output2);
        }

        [Test]
        public void RoundTripWithLoopsAndControlFlow()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun sum_to [n]
                    total = 0
                    loop i in 0..n
                        total += i
                    end
                    print total
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("sum_to", 1), WarValue.FromNumeric(10));
            Assert.AreEqual(new[] { "45" }, output2);
        }

        [Test]
        public void RoundTripWithExceptionHandling()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun safe_div [a, b]
                    begin
                        if b == 0
                            raise ""div by zero""
                        end
                        print a / b
                    rescue e
                        print ""error: "" + e
                    end
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("safe_div", 2),
                WarValue.FromNumeric(10), WarValue.FromNumeric(0));
            Assert.AreEqual(new[] { "error: div by zero" }, output2);
        }

        // ── Classes ──

        [Test]
        public void RoundTripWithClassAndMethod()
        {
            var (script1, _) = TestHelper.Run("test", @"
                class Entity [name, hp]
                    fun status []
                        print this :: name + "": "" + this :: hp
                    end
                end
                fun make_entity []
                    e = new Entity [""Hero"", 100]
                    e :: status []
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("make_entity", 0));
            Assert.AreEqual(new[] { "Hero: 100" }, output2);
        }

        [Test]
        public void RoundTripWithInheritance()
        {
            var (script1, _) = TestHelper.Run("test", @"
                class Animal [name]
                    fun speak []
                        return this :: name + "" speaks""
                    end
                end
                class Dog [name] : Animal [name]
                end
                fun test_dog []
                    d = new Dog [""Rex""]
                    print d :: speak []
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.Call(script2.GetFunction("test_dog", 0));
            Assert.AreEqual(new[] { "Rex speaks" }, output2);
        }

        // ── State preservation ──

        [Test]
        public void LoadBytecodePreservesGlobalVariables()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", @"
                hp = 100
                fun damage [n]
                    hp -= n
                end
                fun get_hp []
                    print hp
                end
            ", null, (s, msg) => output.Add(msg));
            script.Run();

            // Mutate state
            script.Call(script.GetFunction("damage", 1), WarValue.FromNumeric(30));

            // Save bytecode
            var ms = new MemoryStream();
            script.SaveBytecode(ms);

            // Load bytecode back into the SAME script (preserves MemoryScope)
            output.Clear();
            script.LoadBytecode(new MemoryStream(ms.ToArray()));

            // hp should still be 70 (100 - 30), not reset
            script.Call(script.GetFunction("get_hp", 0));
            Assert.AreEqual(new[] { "70" }, output);
        }

        [Test]
        public void LoadBytecodeClearsOldDefinitions()
        {
            var (script, _) = TestHelper.Run("test", @"
                fun old_func []
                    print ""old""
                end
            ");

            Assert.IsTrue(script.HasFunction("old_func", 0));

            // Save different bytecode from a separate script
            var (script2, _) = TestHelper.Run("test2", @"
                fun new_func []
                    print ""new""
                end
            ");
            var ms = new MemoryStream();
            script2.SaveBytecode(ms);

            // Load into original script
            script.LoadBytecode(new MemoryStream(ms.ToArray()));

            Assert.IsFalse(script.HasFunction("old_func", 0));
            Assert.IsTrue(script.HasFunction("new_func", 0));
        }

        [Test]
        public void LoadBytecodeStopsCoroutines()
        {
            var (script, _) = TestHelper.Run("test", @"
                fun ticker []
                    print ""tick""
                    yield
                    print ""tick""
                end
            ");

            script.StartCoroutine("ticker", System.Array.Empty<WarValue>(), loop: true);
            Assert.AreEqual(1, script.ActiveCoroutineCount);

            var ms = new MemoryStream();
            script.SaveBytecode(ms);
            script.LoadBytecode(new MemoryStream(ms.ToArray()));

            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        // ── Binary format integrity ──

        [Test]
        public void SaveProducesNonEmptyBytes()
        {
            var (script, _) = TestHelper.Run("test", @"
                fun f []
                    print 1
                end
            ");

            var ms = new MemoryStream();
            script.SaveBytecode(ms);
            Assert.IsTrue(ms.ToArray().Length > 10); // header alone is 5 bytes
        }

        [Test]
        public void LoadRejectsBadMagic()
        {
            var bad = new byte[] { 0, 0, 0, 0, 1 };
            Assert.Throws<InvalidDataException>(() =>
            {
                var script = new WarScriptLanguage("test", "", null, null);
                script.LoadBytecode(new MemoryStream(bad));
            });
        }

        [Test]
        public void MultipleRoundTrips()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun f [x]
                    print x * 2
                end
            ");

            // Round-trip twice
            var (script2, _) = RoundTrip(script1);
            var (script3, output3) = RoundTrip(script2);

            script3.Call(script3.GetFunction("f", 1), WarValue.FromNumeric(21));
            Assert.AreEqual(new[] { "42" }, output3);
        }

        // ── Coroutines from deserialized bytecode ──

        [Test]
        public void CoroutineWorksAfterLoadBytecode()
        {
            var (script1, _) = TestHelper.Run("test", @"
                fun patrol []
                    loop i in 0..3
                        print ""step "" + i
                        yield
                    end
                end
            ");

            var (script2, output2) = RoundTrip(script1);
            script2.StartCoroutine("patrol", System.Array.Empty<WarValue>());
            Assert.AreEqual(new[] { "step 0" }, output2);

            script2.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 0", "step 1" }, output2);

            script2.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(new[] { "step 0", "step 1", "step 2" }, output2);

            script2.TickCoroutines(F64.FromDouble(0.016));
            Assert.AreEqual(0, script2.ActiveCoroutineCount);
        }

        // ── Helper ──

        /// <summary>
        /// Save bytecode from script1, create a fresh script2, load into it.
        /// </summary>
        private static (WarScriptLanguage script, List<string> output) RoundTrip(
            WarScriptLanguage source)
        {
            var ms = new MemoryStream();
            source.SaveBytecode(ms);
            var bytes = ms.ToArray();

            var output = new List<string>();
            var target = new WarScriptLanguage("test", "", null,
                (s, msg) => output.Add(msg));
            WarScriptLibraryRegistry.RegisterAll(target, target.GlobalDefinitionScope);
            target.Run();
            target.LoadBytecode(new MemoryStream(bytes));

            return (target, output);
        }
    }
}
