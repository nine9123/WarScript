using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    [TestFixture]
    public class ExecutionTests
    {
        // Arithmetic

        [Test]
        public void Addition()
        {
            var (_, output) = TestHelper.Run("test", "print 2 + 3");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Subtraction()
        {
            var (_, output) = TestHelper.Run("test", "print 10 - 4");
            Assert.AreEqual(new[] { "6" }, output);
        }

        [Test]
        public void Multiplication()
        {
            var (_, output) = TestHelper.Run("test", "print 3 * 7");
            Assert.AreEqual(new[] { "21" }, output);
        }

        [Test]
        public void Division()
        {
            var (_, output) = TestHelper.Run("test", "print 15 / 4");
            Assert.AreEqual(new[] { "3.75" }, output);
        }

        [Test]
        public void Modulo()
        {
            var (_, output) = TestHelper.Run("test", "print 10 % 3");
            Assert.AreEqual(new[] { "1" }, output);
        }

        [Test]
        public void StringRepeat()
        {
            var (_, output) = TestHelper.Run("test", "print \"ab\" * 3");
            Assert.AreEqual(new[] { "ababab" }, output);
        }

        [Test]
        public void StringSubtraction()
        {
            var (_, output) = TestHelper.Run("test", "print \"hello world\" - \"world\"");
            Assert.AreEqual(new[] { "hello " }, output);
        }

        // Variables & Scoping

        [Test]
        public void VariableAssignmentAndRetrieval()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                print x
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void InnerScopeShadowsOuter()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", "print x");
            Assert.AreEqual(new[] { "null" }, output);
        }

        // Conditions

        [Test]
        public void IfTrue()
        {
            var (_, output) = TestHelper.Run("test", @"
                if true
                    print ""yes""
                end
            ");
            Assert.AreEqual(new[] { "yes" }, output);
        }

        [Test]
        public void IfFalseElse()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
                loop i in 0..10 by 3
                    print i
                end
            ");
            Assert.AreEqual(new[] { "0", "3", "6", "9" }, output);
        }

        [Test]
        public void IterableLoop()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
                class Vec2 [x, y]
                    fun length []
                        return pow[pow[x, 2] + pow[y, 2], 0.5]
                    end
                end
                v = new Vec2 [3, 4]
                print v :: length []
            ", delegate(WarScriptLanguage script, DefinitionScope scope)
            {
                MathLibrary.Register(script, scope);
            });
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void ClassPropertyMutation()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
                arr = {10, 20, 30}
                print arr{0}
                print arr{2}
            ");
            Assert.AreEqual(new[] { "10", "30" }, output);
        }

        [Test]
        public void ArrayAppend()
        {
            var (_, output) = TestHelper.Run("test", @"
                arr = {1, 2}
                arr << 3
                print arr
            ");
            Assert.AreEqual(new[] { "[1, 2, 3]" }, output);
        }

        [Test]
        public void ArrayConcatenation()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", "print true and false");
            Assert.AreEqual(new[] { "False" }, output);
        }

        [Test]
        public void LogicalOr()
        {
            var (_, output) = TestHelper.Run("test", "print false or true");
            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void LogicalNot()
        {
            var (_, output) = TestHelper.Run("test", "print !true");
            Assert.AreEqual(new[] { "False" }, output);
        }

        [Test]
        public void ShortCircuitAndSkipsRight()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 0
                fun set_x []
                    x = 1
                    return true
                end
                result = false and set_x []
                print x
            ");
            Assert.AreEqual(new[] { "0" }, output);
        }

        [Test]
        public void ShortCircuitOrSkipsRight()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 0
                fun set_x []
                    x = 1
                    return true
                end
                result = true or set_x []
                print x
            ");
            Assert.AreEqual(new[] { "0" }, output);
        }

        // Comparison

        [Test]
        public void EqualsAndNotEquals()
        {
            var (_, output) = TestHelper.Run("test", @"
                print 5 == 5
                print 5 != 3
                print ""a"" == ""a""
            ");
            Assert.AreEqual(new[] { "True", "True", "True" }, output);
        }

        [Test]
        public void NullEquality()
        {
            var (_, output) = TestHelper.Run("test", @"
                print null == null
                print null != 5
            ");
            Assert.AreEqual(new[] { "True", "True" }, output);
        }

        // String Concatenation

        [Test]
        public void StringConcat()
        {
            var (_, output) = TestHelper.Run("test", "print \"hello\" + \" \" + \"world\"");
            Assert.AreEqual(new[] { "hello world" }, output);
        }

        [Test]
        public void StringAndNumericConcat()
        {
            var (_, output) = TestHelper.Run("test", "print \"age: \" + 25");
            Assert.AreEqual(new[] { "age: 25" }, output);
        }

        // Exception Handling

        [Test]
        public void RaiseAndRescue()
        {
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
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
            var (_, output) = TestHelper.Run("test", @"
                assert 1 == 1
                print ""ok""
            ");
            Assert.AreEqual(new[] { "ok" }, output);
        }

        // Native Function Binding

        [Test]
        public void NativeFunctionBinding()
        {
            var (_, output) = TestHelper.Run("test", "print max [21, 2]", delegate(WarScriptLanguage script, DefinitionScope scope)
            {
                MathLibrary.Register(script, scope);
            });
            Assert.AreEqual(new[] { "21" }, output);
        }

        // Call API (game engine tick entry point)

        [Test]
        public void CallFunctionFromEngine()
        {
            var (script, output) = TestHelper.Run("test", @"
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
            var (script, output) = TestHelper.Run("test", @"
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
            var (script, output) = TestHelper.Run("test", @"
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
        
        [Test]
        public void CompoundAdditionAssignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x += 5
                print x
            ");
            Assert.AreEqual(new[] { "15" }, output);
        }

        [Test]
        public void CompoundSubtractionAssignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x -= 3
                print x
            ");
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void CompoundMultiplicationAssignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x *= 4
                print x
            ");
            Assert.AreEqual(new[] { "40" }, output);
        }

        [Test]
        public void CompoundDivisionAssignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x /= 4
                print x
            ");
            Assert.AreEqual(new[] { "2.5" }, output);
        }

        [Test]
        public void CompoundAssignmentInLoop()
        {
            var (_, output) = TestHelper.Run("test", @"
                sum = 0
                loop i in 0..5
                    sum += i
                end
                print sum
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void CompoundAssignmentOnClassProperty()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Entity [hp]
                end
                e = new Entity [100]
                e :: hp -= 25
                print e :: hp
            ");
            Assert.AreEqual(new[] { "75" }, output);
        }

        [Test]
        public void CompoundAssignmentStringConcat()
        {
            var (_, output) = TestHelper.Run("test", @"
                msg = ""hello""
                msg += "" world""
                print msg
            ");
            Assert.AreEqual(new[] { "hello world" }, output);
        }
        
        // ── Compound Assignment ──

        [Test]
        public void CompoundAddition()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x += 5
                print x
            ");
            Assert.AreEqual(new[] { "15" }, output);
        }

        [Test]
        public void CompoundSubtraction()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x -= 3
                print x
            ");
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void CompoundMultiplication()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x *= 4
                print x
            ");
            Assert.AreEqual(new[] { "40" }, output);
        }

        [Test]
        public void CompoundDivision()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x /= 4
                print x
            ");
            Assert.AreEqual(new[] { "2.5" }, output);
        }

        [Test]
        public void CompoundAssignmentChained()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 100
                x += 10
                x -= 5
                x *= 2
                x /= 5
                print x
            ");
            Assert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void CompoundAssignmentInWhileLoop()
        {
            var (_, output) = TestHelper.Run("test", @"
                count = 100
                loop count > 0
                    count -= 10
                end
                print count
            ");
            Assert.AreEqual(new[] { "0" }, output);
        }

        [Test]
        public void CompoundAssignmentWithExpression()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                x += 2 * 3
                print x
            ");
            Assert.AreEqual(new[] { "16" }, output);
        }

        [Test]
        public void CompoundAssignmentArrayElement()
        {
            var (_, output) = TestHelper.Run("test", @"
                arr = {10, 20, 30}
                arr{1} += 5
                print arr{1}
            ");
            Assert.AreEqual(new[] { "25" }, output);
        }

        [Test]
        public void CompoundAssignmentInsideFunction()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun accumulate [n]
                    total = 0
                    loop i in 0..n
                        total += i
                    end
                    return total
                end
                print accumulate [10]
            ");
            Assert.AreEqual(new[] { "45" }, output);
        }
        
        // ── Unary Minus ──

        [Test]
        public void UnaryMinus_NegativeLiteral()
        {
            // This works — lexer parses -5 as a single negative numeric token
            var (_, output) = TestHelper.Run("test", "print -5");
            Assert.AreEqual(new[] { "-5" }, output);
        }

        [Test]
        public void UnaryMinus_NegateVariable()
        {
            // This is the broken case — needs unary minus operator
            var (_, output) = TestHelper.Run("test", @"
                x = 5
                y = -x
                print y
            ");
            Assert.AreEqual(new[] { "-5" }, output);
        }

        [Test]
        public void UnaryMinus_InExpression()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 5
                y = 10 + -x
                print y
            ");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void UnaryMinus_NegateParenthesized()
        {
            var (_, output) = TestHelper.Run("test", @"
                y = -(3 + 4)
                print y
            ");
            Assert.AreEqual(new[] { "-7" }, output);
        }

        [Test]
        public void UnaryMinus_NegativeLiteralInArray()
        {
            var (_, output) = TestHelper.Run("test", @"
                arr = {1, -2, 3}
                print arr{1}
            ");
            Assert.AreEqual(new[] { "-2" }, output);
        }

        [Test]
        public void UnaryMinus_NegateFunctionResult()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun five []
                    return 5
                end
                print -five []
            ");
            Assert.AreEqual(new[] { "-5" }, output);
        }

        [Test]
        public void UnaryMinus_DoubleNegation()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 5
                y = -(-x)
                print y
            ");
            Assert.AreEqual(new[] { "5" }, output);
        }
        
        // ── String Interpolation ──

        [Test]
        public void InterpolationSimpleVariable()
        {
            var (_, output) = TestHelper.Run("test", @"
                name = ""world""
                print ""hello {name}""
            ");
            Assert.AreEqual(new[] { "hello world" }, output);
        }

        [Test]
        public void InterpolationMultipleVariables()
        {
            var (_, output) = TestHelper.Run("test", @"
                name = ""Steve""
                age = 25
                print ""{name} is {age} years old""
            ");
            Assert.AreEqual(new[] { "Steve is 25 years old" }, output);
        }

        [Test]
        public void InterpolationWithExpression()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 5
                print ""value: {x + 1}""
            ");
            Assert.AreEqual(new[] { "value: 6" }, output);
        }

        [Test]
        public void InterpolationWithClassProperty()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Entity [name, hp]
                end
                e = new Entity [""Hero"", 100]
                print ""{e :: name} has {e :: hp} hp""
            ");
            Assert.AreEqual(new[] { "Hero has 100 hp" }, output);
        }

        [Test]
        public void InterpolationWithFunctionCall()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun double [n]
                    return n * 2
                end
                print ""result: {double [5]}""
            ");
            Assert.AreEqual(new[] { "result: 10" }, output);
        }

        [Test]
        public void InterpolationAtStart()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 42
                print ""{x} is the answer""
            ");
            Assert.AreEqual(new[] { "42 is the answer" }, output);
        }

        [Test]
        public void InterpolationAtEnd()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 42
                print ""the answer is {x}""
            ");
            Assert.AreEqual(new[] { "the answer is 42" }, output);
        }

        [Test]
        public void InterpolationOnly()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 42
                print ""{x}""
            ");
            Assert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void InterpolationNested()
        {
            // Array access inside interpolation — tests brace depth tracking
            var (_, output) = TestHelper.Run("test", @"
                arr = {10, 20, 30}
                print ""value: {arr{1}}""
            ");
            Assert.AreEqual(new[] { "value: 20" }, output);
        }

        [Test]
        public void InterpolationNoExpression()
        {
            // Plain string with no {} — unchanged behavior
            var (_, output) = TestHelper.Run("test", "print \"hello world\"");
            Assert.AreEqual(new[] { "hello world" }, output);
        }

        [Test]
        public void InterpolationEmpty()
        {
            var (_, output) = TestHelper.Run("test", "print \"\"");
            Assert.AreEqual(new[] { "" }, output);
        }

        [Test]
        public void InterpolationComplex()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Unit [name, hp, max_hp]
                end
                u = new Unit [""Warrior"", 75, 100]
                print ""{u :: name}: {u :: hp}/{u :: max_hp}""
            ");
            Assert.AreEqual(new[] { "Warrior: 75/100" }, output);
        }

        [Test]
        public void InterpolationInAssignment()
        {
            var (_, output) = TestHelper.Run("test", @"
                name = ""Steve""
                msg = ""hello {name}""
                print msg
            ");
            Assert.AreEqual(new[] { "hello Steve" }, output);
        }

        [Test]
        public void InterpolationWithComparison()
        {
            var (_, output) = TestHelper.Run("test", @"
                hp = 30
                max_hp = 100
                status = ""HP is {hp} which is low""
                print status
            ");
            Assert.AreEqual(new[] { "HP is 30 which is low" }, output);
        }
    }
}
