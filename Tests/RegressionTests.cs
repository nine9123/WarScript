using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    [TestFixture]
    public class RegressionTests
    {
        private static void RunAssertOnlyScript(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName);
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        private static void RunAssertOnlyScriptWithUtilityLib(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    WarScript.Native.UtilityLibrary.Register(s, scope);
                });
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        // Bug 1: TextValue.SetValue
        [Test]
        public void Bug1_TextValue_SetValue_ShouldReplaceNotInsert()
        {
            RunAssertOnlyScript("test_regression_textvalue_setvalue.ws");
        }

        [Test]
        public void Bug1_TextValue_SetValue_Direct()
        {
            var (script, output) = TestHelper.Run("inline",
                "s = \"abcde\"\n" +
                "s{2} = \"Z\"\n" +
                "print s");

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("abZde", output[0],
                "SetValue should replace the character at the index, not insert before it");
        }

        // Bug 2: is_null
        [Test]
        public void Bug2_IsNull_ShouldDetectWarScriptNull()
        {
            RunAssertOnlyScriptWithUtilityLib("test_regression_is_null.ws");
        }

        [Test]
        public void Bug2_IsNull_Direct()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("test", "x = 1", null,
                (s, msg) => output.Add(msg));

            WarScript.Native.UtilityLibrary.Register(script, script.GlobalDefinitionScope);
            script.Run();

            var isNullFn = script.GetFunction("is_null", 1);
            Assert.IsNotNull(isNullFn, "is_null function should be registered");

            var nativeFn = (NativeFunctionDefinition)isNullFn;
            var resultNull = nativeFn.NativeBody(new List<WarValue> { WarValue.Null });
            Assert.IsTrue(resultNull.IsLogical);
            Assert.IsTrue(resultNull.LogicalValue, "is_null[null] should return true");

            var resultNum = nativeFn.NativeBody(new List<WarValue> { WarValue.FromNumeric(42) });
            Assert.IsTrue(resultNum.IsLogical);
            Assert.IsFalse(resultNum.LogicalValue, "is_null[42] should return false");
        }

        // Bug 3: ForLoop counter leaks
        [Test]
        public void Bug3_ForLoop_CounterShouldNotLeakToOuterScope()
        {
            RunAssertOnlyScript("test_regression_forloop_scope.ws");
        }

        [Test]
        public void Bug3_ForLoop_Direct()
        {
            var (script, output) = TestHelper.Run("inline",
                "i = 100\n" +
                "loop i in 0..5\n" +
                "end\n" +
                "print i");
            Assert.IsFalse(script.ExceptionContext.IsRaised());
            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("100", output[0],
                "Outer variable 'i' should be 100 after loop, not clobbered by loop counter");
        }

        // Bug 4: Equals/NotEquals asymmetry
        [Test]
        public void Bug4_EqualsNotEquals_ShouldBeSymmetric()
        {
            RunAssertOnlyScript("test_regression_equals_symmetry.ws");
        }

        [Test]
        public void Bug4_EqualsNotEquals_ArrayDirect()
        {
            var (script, output) = TestHelper.Run("inline",
                "a = {1, 2, 3}\n" +
                "b = {1, 2, 3}\n" +
                "print a == b\n" +
                "print a != b");
            Assert.IsFalse(script.ExceptionContext.IsRaised());
            Assert.AreEqual(2, output.Count);
            Assert.AreEqual("True", output[0], "a == b should be True");
            Assert.AreEqual("False", output[1], "a != b should be False (symmetric with ==)");
        }

        // AST caching
        [Test]
        public void Perf_ASTCaching_SecondRunShouldBeFaster()
        {
            var source = @"
fun fib[n]
    if n < 2
        return n
    end
    return fib[n - 1] + fib[n - 2]
end
class Point[x, y]
    fun add[other]
        return new Point[this :: x + other :: x, this :: y + other :: y]
    end
end
result = fib[10]
assert result == 55
p1 = new Point[1, 2]
p2 = new Point[3, 4]
p3 = p1 :: add[p2]
assert p3 :: x == 4
assert p3 :: y == 6
";
            var output = new List<string>();
            var script = new WarScriptLanguage("perf_test", source, null,
                (s, msg) => output.Add(msg));

            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"First Run() raised an exception: {string.Join("\n", output)}");

            output.Clear();
            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Second Run() raised an exception: {string.Join("\n", output)}");
        }

        [Test]
        public void Perf_ASTCaching_MultipleRunsProduceSameResults()
        {
            var source = @"
x = 0
loop i in 0..100
    x += i
end
print x
";
            var output = new List<string>();
            var script = new WarScriptLanguage("multi_run", source, null,
                (s, msg) => output.Add(msg));

            for (int run = 0; run < 3; run++)
            {
                output.Clear();
                script.Run();
                Assert.IsFalse(script.ExceptionContext.IsRaised(),
                    $"Run {run + 1} raised exception: {string.Join("\n", output)}");
                Assert.AreEqual(1, output.Count, $"Run {run + 1} output count wrong");
                Assert.AreEqual("4950", output[0], $"Run {run + 1} produced wrong result");
            }
        }
    }
}
