using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Gap-filling tests that cover blind spots found in the existing 264-test suite.
    ///
    /// The original tests were thorough within each feature but missed:
    ///   - Mutation paths (string index write, never tested)
    ///   - Entire native functions (is_null never called)
    ///   - Same-name variable conflicts (loop counter vs outer variable)
    ///   - Equality symmetry on complex types (!=  only tested on unequal values)
    ///   - Cross-feature interactions (exception + loop + class combos)
    ///   - AST caching correctness across multiple Run() calls
    /// </summary>
    [TestFixture]
    public class GapFillTests
    {
        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────

        private static void RunAssertOnly(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName);
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"'{resourceName}' raised exception. Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"'{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        private static void RunAssertOnlyWithAllLibs(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    WarScriptLibraryRegistry.RegisterAll(s, scope);
                });
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"'{resourceName}' raised exception. Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"'{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        // ────────────────────────────────────────────────
        //  String mutation (completely untested before)
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_StringMutation()
        {
            RunAssertOnly("test_gapfill_string_mutation.ws");
        }

        // ────────────────────────────────────────────────
        //  Native function coverage (is_null was untested)
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_NativeFunctions()
        {
            RunAssertOnlyWithAllLibs("test_gapfill_native_functions.ws");
        }

        // ────────────────────────────────────────────────
        //  Scope isolation (same-name conflict was untested)
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_ScopeIsolation()
        {
            RunAssertOnly("test_gapfill_scope_isolation.ws");
        }

        // ────────────────────────────────────────────────
        //  Equality symmetry (!=  on equal values was untested)
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_EqualitySymmetry()
        {
            RunAssertOnlyWithAllLibs("test_gapfill_equality_symmetry.ws");
        }

        // ────────────────────────────────────────────────
        //  Cross-feature interactions
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_CrossFeature()
        {
            RunAssertOnlyWithAllLibs("test_gapfill_cross_feature.ws");
        }

        // ────────────────────────────────────────────────
        //  AST caching — run same script multiple times
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_ASTCaching_SingleScript()
        {
            RunAssertOnly("test_gapfill_ast_caching.ws");
        }

        [Test]
        public void GapFill_ASTCaching_MultipleRuns()
        {
            // Run the same script 5 times, each should produce identical results
            var source = System.IO.File.ReadAllText(
                GetResourcePath("test_gapfill_ast_caching.ws"));

            var output = new List<string>();
            var script = new WarScriptLanguage("ast_cache_test", source, null,
                (s, msg) => output.Add(msg));

            for (int run = 1; run <= 5; run++)
            {
                output.Clear();
                script.Run();
                Assert.IsFalse(script.ExceptionContext.IsRaised(),
                    $"Run {run} raised exception: {string.Join("\n", output)}");
                Assert.IsEmpty(output,
                    $"Run {run} produced output: {string.Join("\n", output)}");
            }
        }

        [Test]
        public void GapFill_ASTCaching_PrintConsistency()
        {
            var source = @"
total = 0
loop i in 0..10
    total += i
end
print total
";
            var output = new List<string>();
            var script = new WarScriptLanguage("print_test", source, null,
                (s, msg) => output.Add(msg));

            for (int run = 1; run <= 3; run++)
            {
                output.Clear();
                script.Run();
                Assert.IsFalse(script.ExceptionContext.IsRaised(),
                    $"Run {run} raised exception");
                Assert.AreEqual(1, output.Count, $"Run {run}: expected 1 output line");
                Assert.AreEqual("45", output[0], $"Run {run}: wrong result");
            }
        }

        [Test]
        public void GapFill_ASTCaching_FunctionsPersist()
        {
            // Functions defined in Run() 1 should work in Run() 2
            var source = @"
fun square[n]
    return n * n
end
assert square[5] == 25
assert square[0] == 0
";
            var output = new List<string>();
            var script = new WarScriptLanguage("func_persist", source, null,
                (s, msg) => output.Add(msg));

            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised());

            // Second run should still have square defined and work
            output.Clear();
            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Second run failed: {string.Join("\n", output)}");
        }

        [Test]
        public void GapFill_ASTCaching_ClassesPersist()
        {
            var source = @"
class Vec[x, y]
end
v = new Vec[3, 4]
assert v :: x == 3
assert v :: y == 4
";
            var output = new List<string>();
            var script = new WarScriptLanguage("class_persist", source, null,
                (s, msg) => output.Add(msg));

            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised());

            output.Clear();
            script.Run();
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Second run failed: {string.Join("\n", output)}");
        }

        // ────────────────────────────────────────────────
        //  Direct unit tests for specific edge cases
        // ────────────────────────────────────────────────

        [Test]
        public void GapFill_StringIndexReplace_DoesNotChangeLength()
        {
            var (script, output) = TestHelper.Run("inline",
                "s = \"abcde\"\n" +
                "s{2} = \"X\"\n" +
                "print s\n" +
                "print s{0}\n" +
                "print s{2}\n" +
                "print s{4}");

            Assert.AreEqual(4, output.Count);
            Assert.AreEqual("abXde", output[0], "String should be 'abXde' after replace at index 2");
            Assert.AreEqual("a", output[1]);
            Assert.AreEqual("X", output[2]);
            Assert.AreEqual("e", output[3]);
        }

        [Test]
        public void GapFill_ForLoop_CounterDoesNotClobberOuter()
        {
            var (script, output) = TestHelper.Run("inline",
                "x = \"preserved\"\n" +
                "loop x in 0..10\n" +
                "end\n" +
                "print x");

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("preserved", output[0]);
        }

        [Test]
        public void GapFill_IterableLoop_CounterDoesNotClobberOuter()
        {
            var (script, output) = TestHelper.Run("inline",
                "item = \"preserved\"\n" +
                "loop item in {10, 20, 30}\n" +
                "end\n" +
                "print item");

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("preserved", output[0]);
        }

        [Test]
        public void GapFill_NotEquals_EqualArrays_ReturnsFalse()
        {
            var (script, output) = TestHelper.Run("inline",
                "a = {1, 2, 3}\n" +
                "b = {1, 2, 3}\n" +
                "print a == b\n" +
                "print a != b");

            Assert.AreEqual(2, output.Count);
            Assert.AreEqual("True", output[0], "== on equal arrays should be True");
            Assert.AreEqual("False", output[1], "!= on equal arrays should be False");
        }

        [Test]
        public void GapFill_NotEquals_EqualClasses_ReturnsFalse()
        {
            var (script, output) = TestHelper.Run("inline",
                "class P[x, y]\n" +
                "end\n" +
                "a = new P[1, 2]\n" +
                "b = new P[1, 2]\n" +
                "print a == b\n" +
                "print a != b");

            Assert.AreEqual(2, output.Count);
            Assert.AreEqual("True", output[0], "== on equal class instances should be True");
            Assert.AreEqual("False", output[1], "!= on equal class instances should be False");
        }

        [Test]
        public void GapFill_IsNull_WithNullValue()
        {
            var (script, output) = TestHelper.Run("inline",
                "print is_null[null]\n" +
                "print is_null[0]\n" +
                "print is_null[\"\"]\n" +
                "x = null\n" +
                "print is_null[x]",
                (s, scope) => WarScript.Native.UtilityLibrary.Register(s, scope));

            Assert.AreEqual(4, output.Count);
            Assert.AreEqual("True", output[0], "is_null[null] should be True");
            Assert.AreEqual("False", output[1], "is_null[0] should be False");
            Assert.AreEqual("False", output[2], "is_null[\"\"] should be False");
            Assert.AreEqual("True", output[3], "is_null[x] where x=null should be True");
        }

        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────

        private static string GetResourcePath(string name,
            [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var testDir = System.IO.Path.GetDirectoryName(sourceFilePath)!;
            var path = System.IO.Path.Combine(testDir, "resources", name);
            if (System.IO.File.Exists(path)) return path;
            path = System.IO.Path.Combine("resources", name);
            if (System.IO.File.Exists(path)) return path;
            throw new System.IO.FileNotFoundException($"Resource '{name}' not found");
        }
    }
}
