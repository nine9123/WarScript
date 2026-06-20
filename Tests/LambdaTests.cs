using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class LambdaTests
    {
        [Test]
        public void BasicLambda()
        {
            var (_, output) = TestHelper.Run("test", @"
                double = fun [x] return x * 2 end
                print double [5]
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void LambdaMultipleParams()
        {
            var (_, output) = TestHelper.Run("test", @"
                add = fun [a, b] return a + b end
                print add [3, 4]
            ");
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void LambdaNoParams()
        {
            var (_, output) = TestHelper.Run("test", @"
                greet = fun [] return ""hello"" end
                print greet []
            ");
            Assert.AreEqual(new[] { "hello" }, output);
        }

        [Test]
        public void LambdaAsArgument()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun apply [arr, func]
                    result = {}
                    loop item in arr
                        result << func [item]
                    end
                    return result
                end
                result = apply [{1, 2, 3}, fun [x] return x * 10 end]
                print result{0}
                print result{1}
                print result{2}
            ");
            Assert.AreEqual(new[] { "10", "20", "30" }, output);
        }

        [Test]
        public void LambdaMultiLineBody()
        {
            var (_, output) = TestHelper.Run("test", @"
                sum = fun [n]
                    result = 0
                    loop i in 0..n
                        result = result + i
                    end
                    return result
                end
                print sum [5]
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void LambdaInArray()
        {
            var (_, output) = TestHelper.Run("test", @"
                ops = {
                    fun [a, b] return a + b end,
                    fun [a, b] return a * b end
                }
                f = ops{0}
                print f [3, 4]
                g = ops{1}
                print g [3, 4]
            ");
            Assert.AreEqual(new[] { "7", "12" }, output);
        }

        [Test]
        public void LambdaAsCallback()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun filter [arr, pred]
                    result = {}
                    loop item in arr
                        if pred [item]
                            result << item
                        end
                    end
                    return result
                end
                evens = filter [{1,2,3,4,5,6}, fun [x] return x % 2 == 0 end]
                print evens{0}
                print evens{1}
                print evens{2}
            ");
            Assert.AreEqual(new[] { "2", "4", "6" }, output);
        }

        [Test]
        public void LambdaReassignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                f = fun [x] return x + 1 end
                print f [10]
                f = fun [x] return x * 10 end
                print f [10]
            ");
            Assert.AreEqual(new[] { "11", "100" }, output);
        }

        [Test]
        public void FunctionReturningLambda()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun pick [mode]
                    if mode == ""double""
                        return fun [x] return x * 2 end
                    else
                        return fun [x] return x + 1 end
                    end
                end
                f = pick [""double""]
                print f [5]
                g = pick [""inc""]
                print g [5]
            ");
            Assert.AreEqual(new[] { "10", "6" }, output);
        }

        [Test]
        public void LambdaWithStringOps()
        {
            var (_, output) = TestHelper.Run("test", @"
                tag = fun [prefix, value] return prefix + ""_"" + value end
                print tag [""item"", ""sword""]
            ");
            Assert.AreEqual(new[] { "item_sword" }, output);
        }

        [Test]
        public void FullTestScript()
        {
            var (_, output) = TestHelper.RunFile("test_lambdas.ws");
            Assert.That(output, Does.Contain("all lambda tests passed"));
        }
    }
}
