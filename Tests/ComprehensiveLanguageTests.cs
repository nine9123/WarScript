using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;

namespace Tests
{
    /// <summary>
    /// Comprehensive language test suite.
    ///
    /// Each test method executes a standalone .ws script whose only mechanism
    /// for signalling failure is the <c>assert</c> keyword.  A passing test
    /// means every assert in the script evaluated to true and no unhandled
    /// exception was raised.
    ///
    /// Tests are grouped by language feature area.  The .ws files live in
    /// Tests/resources/ alongside the existing scripts.
    /// </summary>
    [TestFixture]
    public class ComprehensiveLanguageTests
    {
        // ────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────

        /// <summary>
        /// Runs an assert-only script.  Any logger output or a raised
        /// exception means the test failed.
        /// </summary>
        private static void RunAssertOnlyScript(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName);

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        /// <summary>
        /// Runs an assert-only script with the Math native library registered.
        /// </summary>
        private static void RunAssertOnlyScriptWithMath(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    WarScript.Native.MathLibrary.Register(s, scope);
                });

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        /// <summary>
        /// Runs an assert-only script with the Array native library registered.
        /// </summary>
        private static void RunAssertOnlyScriptWithArrayLib(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    WarScript.Native.ArrayLibrary.Register(s, scope);
                });

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        /// <summary>
        /// Runs an assert-only script with all standard libraries registered.
        /// </summary>
        private static void RunAssertOnlyScriptWithAllLibs(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage s, DefinitionScope scope)
                {
                    WarScriptLibraryRegistry.RegisterAll(s, scope);
                });

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception. " +
                $"Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        // ────────────────────────────────────────────────
        //  Arithmetic Operators
        // ────────────────────────────────────────────────

        [Test]
        public void ArithmeticOperators()
        {
            RunAssertOnlyScript("test_arithmetic_operators.ws");
        }

        // ────────────────────────────────────────────────
        //  Comparison Operators
        // ────────────────────────────────────────────────

        [Test]
        public void ComparisonOperators()
        {
            RunAssertOnlyScript("test_comparison_operators.ws");
        }

        // ────────────────────────────────────────────────
        //  Logical Operators
        // ────────────────────────────────────────────────

        [Test]
        public void LogicalOperators()
        {
            RunAssertOnlyScript("test_logical_operators.ws");
        }

        // ────────────────────────────────────────────────
        //  Compound Assignment
        // ────────────────────────────────────────────────

        [Test]
        public void CompoundAssignment()
        {
            RunAssertOnlyScript("test_compound_assignment.ws");
        }

        // ────────────────────────────────────────────────
        //  Variables & Scoping
        // ────────────────────────────────────────────────

        [Test]
        public void VariablesAndScoping()
        {
            RunAssertOnlyScript("test_variables_and_scoping.ws");
        }

        // ────────────────────────────────────────────────
        //  Conditionals
        // ────────────────────────────────────────────────

        [Test]
        public void Conditionals()
        {
            RunAssertOnlyScript("test_conditionals.ws");
        }

        // ────────────────────────────────────────────────
        //  Loops
        // ────────────────────────────────────────────────

        [Test]
        public void Loops()
        {
            RunAssertOnlyScript("test_loops.ws");
        }

        // ────────────────────────────────────────────────
        //  Functions
        // ────────────────────────────────────────────────

        [Test]
        public void Functions()
        {
            RunAssertOnlyScript("test_functions.ws");
        }

        // ────────────────────────────────────────────────
        //  String Operations
        // ────────────────────────────────────────────────

        [Test]
        public void StringOperations()
        {
            RunAssertOnlyScript("test_string_operations.ws");
        }

        // ────────────────────────────────────────────────
        //  String Interpolation
        // ────────────────────────────────────────────────

        [Test]
        public void StringInterpolation()
        {
            RunAssertOnlyScript("test_string_interpolation.ws");
        }

        // ────────────────────────────────────────────────
        //  Arrays
        // ────────────────────────────────────────────────

        [Test]
        public void Arrays()
        {
            RunAssertOnlyScript("test_arrays.ws");
        }

        // ────────────────────────────────────────────────
        //  Classes — Basic
        // ────────────────────────────────────────────────

        [Test]
        public void ClassesBasic()
        {
            RunAssertOnlyScript("test_classes_basic.ws");
        }

        // ────────────────────────────────────────────────
        //  Classes — Inheritance
        // ────────────────────────────────────────────────

        [Test]
        public void ClassesInheritance()
        {
            RunAssertOnlyScript("test_classes_inheritance.ws");
        }

        // ────────────────────────────────────────────────
        //  Classes — Cast & InstanceOf
        // ────────────────────────────────────────────────

        [Test]
        public void ClassesCastAndInstanceOf()
        {
            RunAssertOnlyScript("test_classes_cast_instanceof.ws");
        }

        // ────────────────────────────────────────────────
        //  Nested Classes
        // ────────────────────────────────────────────────

        [Test]
        public void NestedClasses()
        {
            RunAssertOnlyScript("test_nested_classes.ws");
        }

        // ────────────────────────────────────────────────
        //  Exception Handling
        // ────────────────────────────────────────────────

        [Test]
        public void Exceptions()
        {
            RunAssertOnlyScript("test_exceptions.ws");
        }

        // ────────────────────────────────────────────────
        //  Operator Precedence
        // ────────────────────────────────────────────────

        [Test]
        public void OperatorPrecedence()
        {
            RunAssertOnlyScript("test_operator_precedence.ws");
        }

        // ────────────────────────────────────────────────
        //  Null Handling
        // ────────────────────────────────────────────────

        [Test]
        public void NullHandling()
        {
            RunAssertOnlyScript("test_null_handling.ws");
        }

        // ────────────────────────────────────────────────
        //  Edge Cases
        // ────────────────────────────────────────────────

        [Test]
        public void EdgeCases()
        {
            RunAssertOnlyScript("test_edge_cases.ws");
        }

        // ────────────────────────────────────────────────
        //  Math Library (native)
        // ────────────────────────────────────────────────

        [Test]
        public void MathLibrary()
        {
            RunAssertOnlyScriptWithMath("test_math_library.ws");
        }

        // ────────────────────────────────────────────────
        //  Array Library (native)
        // ────────────────────────────────────────────────

        [Test]
        public void ArrayLibrary()
        {
            RunAssertOnlyScriptWithArrayLib("test_array_library.ws");
        }

        // ────────────────────────────────────────────────
        //  Integration (all features combined)
        // ────────────────────────────────────────────────

        [Test]
        public void Integration()
        {
            RunAssertOnlyScriptWithMath("test_integration.ws");
        }
    }
}
