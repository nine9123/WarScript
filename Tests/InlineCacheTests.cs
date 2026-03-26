using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class InlineCacheTests
    {
        // ── Cache hit: same type repeated ──

        [Test]
        public void RepeatedPropertyReadUsesCache()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Entity [name, hp]
                end
                e = new Entity [""Hero"", 100]
                loop i in 0..100
                    print e :: hp
                end
            ");
            Assert.AreEqual(100, output.Count);
            Assert.AreEqual("100", output[0]);
            Assert.AreEqual("100", output[99]);
        }

        [Test]
        public void RepeatedPropertyWriteUsesCache()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Counter [value]
                end
                c = new Counter [0]
                loop i in 0..100
                    c :: value = c :: value + 1
                end
                print c :: value
            ");
            Assert.AreEqual(new[] { "100" }, output);
        }

        [Test]
        public void MethodAccessWithCachedProperties()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Entity [name, hp]
                    fun status []
                        print this :: name + "": "" + this :: hp
                    end
                end
                e = new Entity [""Warrior"", 50]
                loop i in 0..3
                    e :: status []
                end
            ");
            Assert.AreEqual(3, output.Count);
            Assert.AreEqual("Warrior: 50", output[0]);
        }

        // ── Polymorphic access: different types at same bytecode site ──

        [Test]
        public void PolymorphicPropertyAccess()
        {
            // The same GetProperty bytecode site sees different class types.
            // Cache should invalidate and re-fill on type change.
            var (_, output) = TestHelper.Run("test", @"
                class Cat [name, sound]
                end
                class Dog [name, sound]
                end
                fun speak [animal]
                    print animal :: name + "": "" + animal :: sound
                end
                speak [new Cat [""Whiskers"", ""meow""]]
                speak [new Dog [""Rex"", ""woof""]]
                speak [new Cat [""Luna"", ""purr""]]
            ");
            Assert.AreEqual(new[] { "Whiskers: meow", "Rex: woof", "Luna: purr" }, output);
        }

        [Test]
        public void PolymorphicPropertyWrite()
        {
            var (_, output) = TestHelper.Run("test", @"
                class A [x]
                end
                class B [x]
                end
                fun set_x [obj, val]
                    obj :: x = val
                    print obj :: x
                end
                set_x [new A [0], 10]
                set_x [new B [0], 20]
                set_x [new A [0], 30]
            ");
            Assert.AreEqual(new[] { "10", "20", "30" }, output);
        }

        // ── Inheritance ──

        [Test]
        public void InheritedPropertyCached()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Animal [name]
                end
                class Dog [name, breed] : Animal [name]
                end
                d = new Dog [""Rex"", ""Lab""]
                print d :: name
                print d :: breed
            ");
            Assert.AreEqual(new[] { "Rex", "Lab" }, output);
        }

        [Test]
        public void InheritedPropertyMutationPropagates()
        {
            // Shared ValueReferences: mutating via derived updates base
            var (_, output) = TestHelper.Run("test", @"
                class Animal [name, hp]
                end
                class Dog [name, hp, breed] : Animal [name, hp]
                end
                d = new Dog [""Rex"", 100, ""Lab""]
                d :: hp = 50
                base = d as Animal
                print base :: hp
            ");
            Assert.AreEqual(new[] { "50" }, output);
        }

        // ── Multi-property classes ──

        [Test]
        public void MultiPropertyClassCorrectIndices()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Item [name, weight, value, stackable]
                end
                i = new Item [""Sword"", 5, 100, false]
                print i :: name
                print i :: weight
                print i :: value
                print i :: stackable
            ");
            Assert.AreEqual(new[] { "Sword", "5", "100", "False" }, output);
        }

        [Test]
        public void MultiPropertyWriteThenRead()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Vec3 [x, y, z]
                end
                v = new Vec3 [1, 2, 3]
                v :: x = 10
                v :: y = 20
                v :: z = 30
                print v :: x + v :: y + v :: z
            ");
            Assert.AreEqual(new[] { "60" }, output);
        }

        // ── Array property via IndexSetProp ──

        [Test]
        public void IndexSetPropUsesCache()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Grid [cells]
                end
                g = new Grid [{0, 0, 0}]
                g :: cells{1} = 42
                print g :: cells{1}
            ");
            Assert.AreEqual(new[] { "42" }, output);
        }

        // ── Serialization round-trip preserves cache slot count ──

        [Test]
        public void CacheSlotsWorkAfterSerialization()
        {
            var (script1, _) = TestHelper.Run("test", @"
                class Entity [name, hp]
                end
                fun test_entity []
                    e = new Entity [""Hero"", 100]
                    e :: hp = 75
                    print e :: name + "": "" + e :: hp
                end
            ");

            // Save
            var ms = new MemoryStream();
            script1.SaveBytecode(ms);

            // Load into fresh script
            var output2 = new List<string>();
            var script2 = new WarScriptLanguage("test", "", null,
                (s, msg) => output2.Add(msg));
            script2.Run();
            script2.LoadBytecode(new MemoryStream(ms.ToArray()));

            script2.Call(script2.GetFunction("test_entity", 0));
            Assert.AreEqual(new[] { "Hero: 75" }, output2);
        }

        // ── Hot reload clears inline caches ──

        [Test]
        public void CachesWorkAfterReload()
        {
            var (script, output) = TestHelper.Run("test", @"
                class Entity [hp]
                end
                fun get_hp [e]
                    return e :: hp
                end
            ");

            script.Reload(@"
                class Entity [hp]
                end
                fun get_hp [e]
                    return e :: hp
                end
                fun make []
                    e = new Entity [42]
                    print get_hp [e]
                end
            ");
            script.Call(script.GetFunction("make", 0));
            Assert.AreEqual(new[] { "42" }, output);
        }
    }
}
