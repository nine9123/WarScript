using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Regression tests for known bugs and performance improvements.
    /// </summary>
    [TestFixture]
    public class RegressionTests
    {
        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────

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

        // ────────────────────────────────────────────────
        //  Bug 1: TextValue.SetValue inserts instead of replacing
        // ────────────────────────────────────────────────

        [Test]
        public void Bug1_TextValue_SetValue_ShouldReplaceNotInsert()
        {
            RunAssertOnlyScript("test_regression_textvalue_setvalue.ws");
        }

        /// <summary>
        /// Direct unit test for the TextValue.SetValue mechanics.
        /// </summary>
        [Test]
        public void Bug1_TextValue_SetValue_Direct()
        {
            var (script, _) = TestHelper.Run("inline", "x = 1");
            var text = new TextValue(script, "abcde");

            // Replace character at index 2 ('c') with 'Z'
            text.SetValue(2, new TextValue(script, "Z"));
            Assert.AreEqual("abZde", text.GetValue(),
                "SetValue should replace the character at the index, not insert before it");

            // Replace character at index 0
            text.SetValue(0, new TextValue(script, "X"));
            Assert.AreEqual("XbZde", text.GetValue());
        }

        // ────────────────────────────────────────────────
        //  Bug 2: is_null checks CLR null, not NullValue
        // ────────────────────────────────────────────────

        [Test]
        public void Bug2_IsNull_ShouldDetectWarScriptNull()
        {
            RunAssertOnlyScriptWithUtilityLib("test_regression_is_null.ws");
        }

        /// <summary>
        /// Direct test: passing the NullValue singleton to is_null should return true.
        /// Call() is for user-defined functions; native functions must be invoked
        /// via their NativeBody delegate directly.
        /// </summary>
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

            // Invoke the native body directly (Call() only works for user-defined functions)
            var nativeFn = (NativeFunctionDefinition)isNullFn;
            var resultNull = nativeFn.NativeBody(new List<IValue> { script.Null });
            Assert.IsInstanceOf<LogicalValue>(resultNull);
            Assert.IsTrue(((LogicalValue)resultNull).GetValue(),
                "is_null[null] should return true");

            var resultNum = nativeFn.NativeBody(new List<IValue> { new NumericValue(script, 42) });
            Assert.IsInstanceOf<LogicalValue>(resultNum);
            Assert.IsFalse(((LogicalValue)resultNum).GetValue(),
                "is_null[42] should return false");
        }

        // ────────────────────────────────────────────────
        //  Bug 3: ForLoop counter leaks into outer scope
        // ────────────────────────────────────────────────

        [Test]
        public void Bug3_ForLoop_CounterShouldNotLeakToOuterScope()
        {
            RunAssertOnlyScript("test_regression_forloop_scope.ws");
        }

        /// <summary>
        /// Direct test: variable set before loop should survive unchanged.
        /// </summary>
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

        // ────────────────────────────────────────────────
        //  Bug 4: Equals vs NotEquals asymmetry
        // ────────────────────────────────────────────────

        [Test]
        public void Bug4_EqualsNotEquals_ShouldBeSymmetric()
        {
            RunAssertOnlyScript("test_regression_equals_symmetry.ws");
        }

        /// <summary>
        /// Direct test: two arrays with same values must satisfy both == and !(!=).
        /// </summary>
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

        // ────────────────────────────────────────────────
        //  Performance: AST caching across Run() calls
        // ────────────────────────────────────────────────

        [Test]
        public void Perf_ASTCaching_SecondRunShouldBeFaster()
        {
            // A script with enough structure that parse time is measurable
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

            // First run — parses + executes
            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"First Run() raised an exception: {string.Join("\n", output)}");

            // Second run — should reuse cached AST
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

            // Run 3 times, each should produce the same output
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
