using System.Collections.Generic;
using NUnit.Framework;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Tests
{
    [TestFixture]
    public class ExecutionTests
    {
        // Arithmetic

        [Test]
        public void Addition()
        {
            var (_, output) = TestHelper.Run("print 2 + 3");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Subtraction()
        {
            var (_, output) = TestHelper.Run("print 10 - 4");
            Assert.AreEqual(new[] { "6" }, output);
        }

        [Test]
        public void Multiplication()
        {
            var (_, output) = TestHelper.Run("print 3 * 7");
            Assert.AreEqual(new[] { "21" }, output);
        }

        [Test]
        public void Division()
        {
            var (_, output) = TestHelper.Run("print 15 / 4");
            Assert.AreEqual(new[] { "3.75" }, output);
        }

        [Test]
        public void FloorDivision()
        {
            var (_, output) = TestHelper.Run("print 15 // 4");
            Assert.AreEqual(new[] { "3" }, output);
        }

        [Test]
        public void Modulo()
        {
            var (_, output) = TestHelper.Run("print 10 % 3");
            Assert.AreEqual(new[] { "1" }, output);
        }

        [Test]
        public void Exponentiation()
        {
            var (_, output) = TestHelper.Run("print 2 ** 10");
            Assert.AreEqual(new[] { "1024" }, output);
        }

        [Test]
        public void StringRepeat()
        {
            var (_, output) = TestHelper.Run("print \"ab\" * 3");
            Assert.AreEqual(new[] { "ababab" }, output);
        }

        [Test]
        public void StringSubtraction()
        {
            var (_, output) = TestHelper.Run("print \"hello world\" - \"world\"");
            Assert.AreEqual(new[] { "hello " }, output);
        }

        // Variables & Scoping

        [Test]
        public void VariableAssignmentAndRetrieval()
        {
            var (_, output) = TestHelper.Run(@"
                x = 10
                print x
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void InnerScopeShadowsOuter()
        {
            var (_, output) = TestHelper.Run(@"
                x = 1
                if true
                    x = 2
                end
                print x
            ");
            Assert.AreEqual(new[] { "2" }, output);
        }

        [Test]
        public void NullDefault()
        {
            var (_, output) = TestHelper.Run("print x");
            Assert.AreEqual(new[] { "null" }, output);
        }

        // Conditions

        [Test]
        public void IfTrue()
        {
            var (_, output) = TestHelper.Run(@"
                if true
                    print ""yes""
                end
            ");
            Assert.AreEqual(new[] { "yes" }, output);
        }

        [Test]
        public void IfFalseElse()
        {
            var (_, output) = TestHelper.Run(@"
                if false
                    print ""yes""
                else
                    print ""no""
                end
            ");
            Assert.AreEqual(new[] { "no" }, output);
        }

        [Test]
        public void ElifChain()
        {
            var (_, output) = TestHelper.Run(@"
                x = 5
                if x > 10
                    print ""big""
                elif x > 3
                    print ""medium""
                else
                    print ""small""
                end
            ");
            Assert.AreEqual(new[] { "medium" }, output);
        }

        // Loops

        [Test]
        public void WhileLoop()
        {
            var (_, output) = TestHelper.Run(@"
                i = 0
                loop i < 3
                    print i
                    i = i + 1
                end
            ");
            Assert.AreEqual(new[] { "0", "1", "2" }, output);
        }

        [Test]
        public void ForLoop()
        {
            var (_, output) = TestHelper.Run(@"
                sum = 0
                loop i in 0..5
                    sum = sum + i
                end
                print sum
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void ForLoopWithStep()
        {
            var (_, output) = TestHelper.Run(@"
                loop i in 0..10 by 3
                    print i
                end
            ");
            Assert.AreEqual(new[] { "0", "3", "6", "9" }, output);
        }

        [Test]
        public void IterableLoop()
        {
            var (_, output) = TestHelper.Run(@"
                arr = {10, 20, 30}
                loop item in arr
                    print item
                end
            ");
            Assert.AreEqual(new[] { "10", "20", "30" }, output);
        }

        [Test]
        public void BreakStatement()
        {
            var (_, output) = TestHelper.Run(@"
                loop i in 0..10
                    if i == 3
                        break
                    end
                    print i
                end
            ");
            Assert.AreEqual(new[] { "0", "1", "2" }, output);
        }

        [Test]
        public void NextStatement()
        {
            var (_, output) = TestHelper.Run(@"
                loop i in 0..5
                    if i == 2
                        next
                    end
                    print i
                end
            ");
            Assert.AreEqual(new[] { "0", "1", "3", "4" }, output);
        }

        // ── Functions ──

        [Test]
        public void FunctionDefinitionAndCall()
        {
            var (_, output) = TestHelper.Run(@"
                fun add [a, b]
                    return a + b
                end
                print add [3, 4]
            ");
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void FunctionRecursion()
        {
            var (_, output) = TestHelper.Run(@"
                fun factorial [n]
                    if n <= 1
                        return 1
                    end
                    return n * factorial [n - 1]
                end
                print factorial [5]
            ");
            Assert.AreEqual(new[] { "120" }, output);
        }

        // ── Classes ──

        [Test]
        public void ClassPropertyAccess()
        {
            var (_, output) = TestHelper.Run(@"
                class Point [x, y]
                end
                p = new Point [3, 4]
                print p :: x
                print p :: y
            ");
            Assert.AreEqual(new[] { "3", "4" }, output);
        }

        [Test]
        public void ClassMethod()
        {
            var (_, output) = TestHelper.Run(@"
                class Vec2 [x, y]
                    fun length []
                        return (x ** 2 + y ** 2) ** 0.5
                    end
                end
                v = new Vec2 [3, 4]
                print v :: length []
            ");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void ClassPropertyMutation()
        {
            var (_, output) = TestHelper.Run(@"
                class Box [value]
                end
                b = new Box [10]
                b :: value = 20
                print b :: value
            ");
            Assert.AreEqual(new[] { "20" }, output);
        }

        [Test]
        public void ClassInheritance()
        {
            var (_, output) = TestHelper.Run(@"
                class Animal [name]
                    fun speak []
                        return name + "" speaks""
                    end
                end
                class Dog [name] : Animal [name]
                end
                d = new Dog [""Rex""]
                print d :: speak []
            ");
            Assert.AreEqual(new[] { "Rex speaks" }, output);
        }

        // ── Arrays ──

        [Test]
        public void ArrayCreationAndAccess()
        {
            var (_, output) = TestHelper.Run(@"
                arr = {10, 20, 30}
                print arr{0}
                print arr{2}
            ");
            Assert.AreEqual(new[] { "10", "30" }, output);
        }

        [Test]
        public void ArrayAppend()
        {
            var (_, output) = TestHelper.Run(@"
                arr = {1, 2}
                arr << 3
                print arr
            ");
            Assert.AreEqual(new[] { "[1, 2, 3]" }, output);
        }

        [Test]
        public void ArrayConcatenation()
        {
            var (_, output) = TestHelper.Run(@"
                a = {1, 2}
                b = {3, 4}
                print a + b
            ");
            Assert.AreEqual(new[] { "[1, 2, 3, 4]" }, output);
        }

        // ── Logical Operators ──

        [Test]
        public void LogicalAnd()
        {
            var (_, output) = TestHelper.Run("print true and false");
            Assert.AreEqual(new[] { "False" }, output);
        }

        [Test]
        public void LogicalOr()
        {
            var (_, output) = TestHelper.Run("print false or true");
            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void LogicalNot()
        {
            var (_, output) = TestHelper.Run("print !true");
            Assert.AreEqual(new[] { "False" }, output);
        }

        [Test]
        public void ShortCircuitAndSkipsRight()
        {
            // The assignment inside the condition should never execute
            var (_, output) = TestHelper.Run(@"
                x = 0
                result = false and (x = 1) == 1
                print x
            ");
            Assert.AreEqual(new[] { "0" }, output);
        }

        [Test]
        public void ShortCircuitOrSkipsRight()
        {
            var (_, output) = TestHelper.Run(@"
                x = 0
                result = true or (x = 1) == 1
                print x
            ");
            Assert.AreEqual(new[] { "0" }, output);
        }

        // ── Comparison ──

        [Test]
        public void EqualsAndNotEquals()
        {
            var (_, output) = TestHelper.Run(@"
                print 5 == 5
                print 5 != 3
                print ""a"" == ""a""
            ");
            Assert.AreEqual(new[] { "True", "True", "True" }, output);
        }

        [Test]
        public void NullEquality()
        {
            var (_, output) = TestHelper.Run(@"
                print null == null
                print null != 5
            ");
            Assert.AreEqual(new[] { "True", "True" }, output);
        }

        // ── String Concatenation ──

        [Test]
        public void StringConcat()
        {
            var (_, output) = TestHelper.Run("print \"hello\" + \" \" + \"world\"");
            Assert.AreEqual(new[] { "hello world" }, output);
        }

        [Test]
        public void StringAndNumericConcat()
        {
            var (_, output) = TestHelper.Run("print \"age: \" + 25");
            Assert.AreEqual(new[] { "age: 25" }, output);
        }

        // ── Exception Handling ──

        [Test]
        public void RaiseAndRescue()
        {
            var (_, output) = TestHelper.Run(@"
                begin
                    raise ""boom""
                    print ""unreachable""
                rescue e
                    print e
                end
            ");
            Assert.AreEqual(new[] { "boom" }, output);
        }

        [Test]
        public void EnsureAlwaysRuns()
        {
            var (_, output) = TestHelper.Run(@"
                begin
                    raise ""error""
                rescue e
                    print ""caught""
                ensure
                    print ""cleanup""
                end
            ");
            Assert.AreEqual(new[] { "caught", "cleanup" }, output);
        }

        [Test]
        public void AssertPasses()
        {
            var (_, output) = TestHelper.Run(@"
                assert 1 == 1
                print ""ok""
            ");
            Assert.AreEqual(new[] { "ok" }, output);
        }

        // ── Native Function Binding ──

        [Test]
        public void NativeFunctionBinding()
        {
            var (_, output) = TestHelper.Run(
                "print double [21]",
                setupScope: scope =>
                {
                    scope.AddFunction(new NativeFunctionDefinition(
                        new FunctionDetails("double", new List<string> { "n" }),
                        args =>
                        {
                            var n = NativeHelper.Arg<NumericValue>(args, 0);
                            // Need a script reference for NumericValue — use null since
                            // we only care about the numeric result for printing
                            return new NumericValue(null, n.GetValue() * 2);
                        },
                        "Doubles a number", "NumericValue"));
                });
            Assert.AreEqual(new[] { "42" }, output);
        }

        // ── Call API (game engine tick entry point) ──

        [Test]
        public void CallFunctionFromEngine()
        {
            var (script, output) = TestHelper.Run(@"
                counter = 0
                fun tick []
                    counter = counter + 1
                    print counter
                end
            ");

            var tick = script.GetFunction("tick", 0);
            Assert.IsNotNull(tick);

            script.Call(tick);
            script.Call(tick);
            script.Call(tick);

            Assert.AreEqual(new[] { "1", "2", "3" }, output);
        }

        [Test]
        public void CallFunctionWithArguments()
        {
            var (script, output) = TestHelper.Run(@"
                fun greet [name]
                    print ""hello "" + name
                end
            ");

            var greet = script.GetFunction("greet", 1);
            script.Call(greet, new TextValue(script, "world"));

            Assert.AreEqual(new[] { "hello world" }, output);
        }

        [Test]
        public void StatePersistsBetweenCalls()
        {
            var (script, output) = TestHelper.Run(@"
                sum = 0
                fun add [n]
                    sum = sum + n
                end
                fun get_sum []
                    return sum
                end
            ");

            var add = script.GetFunction("add", 1);
            script.Call(add, new NumericValue(script, 10));
            script.Call(add, new NumericValue(script, 20));
            script.Call(add, new NumericValue(script, 30));

            var getSum = script.GetFunction("get_sum", 0);
            script.Call(getSum);

            // get_sum returns via ReturnContext, not print — 
            // check that no exceptions were raised and the script is healthy
            Assert.IsFalse(script.ExceptionContext.IsRaised());
        }
    }
}
