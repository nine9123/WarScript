using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Second layer of comprehensive language tests.
    ///
    /// Deeply exercises every language feature including complex
    /// interactions, algorithms, the 5 fixed parser/runtime bugs,
    /// and the known recursive scoping limitation.
    /// </summary>
    [TestFixture]
    public class DeepLanguageTests
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

        private static void RunAssertOnlyScriptWithMath(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    MathLibrary.Register(s, scope);
                });

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        // ── Deep Classes ──

        [Test]
        public void DeepClasses()
        {
            RunAssertOnlyScriptWithMath("test_deep_classes.ws");
        }

        // ── Deep Scoping ──

        [Test]
        public void DeepScoping()
        {
            RunAssertOnlyScript("test_deep_scoping.ws");
        }

        // ── Deep Exceptions ──

        [Test]
        public void DeepExceptions()
        {
            RunAssertOnlyScript("test_deep_exceptions.ws");
        }

        // ── Deep Operators (exercises all 5 bug fixes) ──

        [Test]
        public void DeepOperators()
        {
            RunAssertOnlyScript("test_deep_operators.ws");
        }

        // ── Deep Arrays ──

        [Test]
        public void DeepArrays()
        {
            RunAssertOnlyScript("test_deep_arrays.ws");
        }

        // ── Deep Property Access (exercises Bug 4 fix) ──

        [Test]
        public void DeepPropertyAccess()
        {
            RunAssertOnlyScript("test_deep_property_access.ws");
        }

        // ── Deep Control Flow ──

        [Test]
        public void DeepControlFlow()
        {
            RunAssertOnlyScript("test_deep_control_flow.ws");
        }

        // ── Deep String Interpolation ──

        [Test]
        public void DeepStringInterpolation()
        {
            RunAssertOnlyScript("test_deep_string_interpolation.ws");
        }

        // ── Recursion (split for memory) ──

        [Test]
        public void DeepRecursionMath()
        {
            RunAssertOnlyScriptWithMath("test_deep_rec_math.ws");
        }

        [Test]
        public void DeepRecursionArrays()
        {
            RunAssertOnlyScript("test_deep_rec_arrays.ws");
        }

        [Test]
        public void DeepRecursionMutual()
        {
            RunAssertOnlyScript("test_deep_rec_mutual.ws");
        }

        [Test]
        public void DeepRecursionAckermann()
        {
            RunAssertOnlyScript("test_deep_rec_ackermann.ws");
        }

        [Test]
        public void DeepRecursionTree()
        {
            RunAssertOnlyScript("test_deep_rec_tree.ws");
        }

        // ── Algorithms (split for memory) ──

        [Test]
        public void DeepAlgoSieve()
        {
            RunAssertOnlyScript("test_deep_algo_sieve.ws");
        }

        [Test]
        public void DeepAlgoCollatz()
        {
            RunAssertOnlyScript("test_deep_algo_collatz.ws");
        }

        [Test]
        public void DeepAlgoRoman()
        {
            RunAssertOnlyScript("test_deep_algo_roman.ws");
        }

        [Test]
        public void DeepAlgoStrings()
        {
            RunAssertOnlyScript("test_deep_algo_strings.ws");
        }

        [Test]
        public void DeepAlgoMatrix()
        {
            RunAssertOnlyScript("test_deep_algo_matrix.ws");
        }

        [Test]
        public void DeepAlgoHashMap()
        {
            RunAssertOnlyScript("test_deep_algo_hashmap.ws");
        }

        [Test]
        public void DeepAlgoMisc()
        {
            RunAssertOnlyScript("test_deep_algo_misc.ws");
        }

        // ── Bug 6 regression: recursive functions with local variables ──

        [Test]
        public void DeepRecursiveLocals()
        {
            RunAssertOnlyScriptWithMath("test_deep_recursive_locals.ws");
        }

        // ── New batch: functional patterns ──

        [Test]
        public void DeepFuncPatterns()
        {
            RunAssertOnlyScriptWithMath("test_deep_func_patterns.ws");
        }

        // ── New batch: class design patterns ──

        [Test]
        public void DeepClassPatterns()
        {
            RunAssertOnlyScript("test_deep_class_patterns.ws");
        }

        // ── New batch: data structures ──

        [Test]
        public void DeepDataStructures()
        {
            RunAssertOnlyScript("test_deep_data_structures.ws");
        }

        // ── New batch: type coercion & dynamic typing ──

        [Test]
        public void DeepTypeCoercion()
        {
            RunAssertOnlyScript("test_deep_type_coercion.ws");
        }

        // ── New batch: multi-inheritance edge cases ──

        [Test]
        public void DeepMultiInherit()
        {
            RunAssertOnlyScript("test_deep_multi_inherit.ws");
        }

        // ── New batch: loop patterns ──

        [Test]
        public void DeepLoopPatterns()
        {
            RunAssertOnlyScript("test_deep_loop_patterns.ws");
        }

        // ── New batch: null handling patterns ──

        [Test]
        public void DeepNullPatterns()
        {
            RunAssertOnlyScript("test_deep_null_patterns.ws");
        }

        // ── New batch: string algorithms ──

        [Test]
        public void DeepStringAlgos()
        {
            RunAssertOnlyScript("test_deep_string_algos.ws");
        }

        // ── New batch: exception patterns ──

        [Test]
        public void DeepExceptionPatterns()
        {
            RunAssertOnlyScript("test_deep_exception_patterns.ws");
        }

        // ── New batch: compound assignment patterns ──

        [Test]
        public void DeepCompoundOps()
        {
            RunAssertOnlyScript("test_deep_compound_ops.ws");
        }

        // ── New batch: class method combinations ──

        [Test]
        public void DeepClassMethodCombos()
        {
            RunAssertOnlyScript("test_deep_class_method_combos.ws");
        }

        // ── New batch: interpolation combinations ──

        [Test]
        public void DeepInterpCombos()
        {
            RunAssertOnlyScript("test_deep_interp_combos.ws");
        }

        // ── New batch: sorting algorithms ──

        [Test]
        public void DeepAlgoSorting()
        {
            RunAssertOnlyScript("test_deep_algo_sorting.ws");
        }

        // ── New batch: real-world scenarios ──

        [Test]
        public void DeepRealWorld()
        {
            RunAssertOnlyScript("test_deep_real_world.ws");
        }
    }
}
