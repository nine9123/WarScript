using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    [TestFixture]
    public class StatementParserTest
    {
        [Test]
        public void Print_Numeric_Expression()
        {
            var (_, output) = TestHelper.Run("test", "print 2 + 3");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Variable_Assignment_And_Retrieval()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 10
                print x
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void Function_Definition_And_Call()
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
        public void For_Loop_Accumulates()
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
        public void Class_Properties_And_Methods()
        {
            var (_, output) = TestHelper.Run("test", @"
                class Vec2 [x, y]
                    fun length []
                        return pow[(pow[x, 2] + pow[y, 2]), 0.5]
                    end
                end
                v = new Vec2 [3, 4]
                print round[v :: length []]
            ", delegate(WarScriptLanguage script, DefinitionScope scope)
            {
                MathLibrary.Register(script, scope);
            });
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Native_Function_Binding()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(
                "test",
                "print double [21]",
                null,
                (s, msg) => output.Add(msg));

            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("double", new List<string> { "n" }),
                args =>
                {
                    var n = NativeHelper.NumericArg(args, 0);
                    return WarValue.FromNumeric(n * 2);
                },
                "Doubles a number", "NumericValue"));
            
            script.Run();

            Assert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void Exception_Is_Caught_In_Rescue()
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
        public void Short_Circuit_And_Skips_Right()
        {
            var (_, output) = TestHelper.Run("test", @"
                x = 0
                fun set_x[]
                    x = 1
                    return true
                end
                if false and set_x[]
                    print ""branch taken""
                end
                print x
            ");
            // x should remain 0: the assignment should never execute
            Assert.AreEqual(new[] { "0" }, output);
        }
    }
}
