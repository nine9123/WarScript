using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class DefaultParameterTests
    {
        [Test]
        public void BasicDefault()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun greet [name, greeting = ""Hello""]
                    return greeting + "", "" + name
                end
                print greet[""World""]
                print greet[""World"", ""Hi""]
            ");
            Assert.AreEqual(new[] { "Hello, World", "Hi, World" }, output);
        }

        [Test]
        public void MultipleDefaults()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun f [a, b = 10, c = 100]
                    return a + b + c
                end
                print f[1]
                print f[1, 2]
                print f[1, 2, 3]
            ");
            Assert.AreEqual(new[] { "111", "103", "6" }, output);
        }

        [Test]
        public void AllParamsOptional()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun f [a = 1, b = 2, c = 3]
                    return a + b + c
                end
                print f[]
                print f[10]
                print f[10, 20]
                print f[10, 20, 30]
            ");
            Assert.AreEqual(new[] { "6", "15", "33", "60" }, output);
        }

        [Test]
        public void DefaultWithExpression()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun f [a, b = 2 + 3]
                    return a + b
                end
                print f[10]
                print f[10, 1]
            ");
            Assert.AreEqual(new[] { "15", "11" }, output);
        }

        [Test]
        public void DefaultWithString()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun tag [value, prefix = ""item_""]
                    return prefix + value
                end
                print tag[""sword""]
                print tag[""sword"", ""weapon_""]
            ");
            Assert.AreEqual(new[] { "item_sword", "weapon_sword" }, output);
        }

        [Test]
        public void DefaultWithBoolean()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun maybe [value, flag = true]
                    if flag
                        return value
                    end
                    return 0
                end
                print maybe[42]
                print maybe[42, false]
            ");
            Assert.AreEqual(new[] { "42", "0" }, output);
        }

        [Test]
        public void ClassMethodWithDefault()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Counter [n]
                    fun add [amount = 1]
                        n = n + amount
                    end
                    fun get []
                        return this :: n
                    end
                end
                c = new Counter [0]
                c :: add []
                c :: add [5]
                print c :: get []
            ");
            Assert.AreEqual(new[] { "6" }, output);
        }

        [Test]
        public void RecursiveWithDefault()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun sum [n, acc = 0]
                    if n == 0
                        return acc
                    end
                    return sum [n - 1, acc + n]
                end
                print sum[5]
                print sum[5, 100]
            ");
            Assert.AreEqual(new[] { "15", "115" }, output);
        }

        [Test]
        public void ExplicitNullGetsDefault()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun f [x, y = 42]
                    return y
                end
                print f[1, null]
            ");
            // Passing null explicitly triggers the default (desugar uses == null check)
            Assert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void OverriddenDefault()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun f [x, y = 42]
                    return y
                end
                print f[1, 99]
            ");
            Assert.AreEqual(new[] { "99" }, output);
        }

        [Test]
        public void RequiredAfterDefaultThrowsSyntaxError()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    fun f [a = 1, b]
                        return a + b
                    end
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void DefaultDoesNotConflictWithOverload()
        {
            // A no-arg function and a separate function with defaults at arity 1+
            // should coexist — the explicit no-arg version wins at arity 0.
            var (_, output) = TestHelper.Run("test", @"
                fun f []
                    return ""no args""
                end
                fun g [a = 1]
                    return a
                end
                print f[]
                print g[]
                print g[99]
            ");
            Assert.AreEqual(new[] { "no args", "1", "99" }, output);
        }

        [Test]
        public void FullTestScript()
        {
            var (_, output) = TestHelper.RunFile("test_default_params.ws");
            Assert.That(output, Does.Contain("all default parameter tests passed"));
        }
    }
}
