using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Integration tests that execute full .ws script files.
    ///
    /// Scripts that only use asserts produce no output: a passing test means
    /// every assert in the script evaluated to true without raising an exception.
    /// Scripts that use print are checked against expected output.
    ///
    /// Place .ws files in Tests/resources/
    /// </summary>
    [TestFixture]
    public class WarScriptLanguageTest
    {
        /// <summary>
        /// Runs a script that uses only asserts — any output means something
        /// went wrong (an unhandled exception was logged).
        /// </summary>
        private static void RunAssertOnlyScript(string resourceName)
        {
            var (script, output) = TestHelper.RunFile(resourceName,
                delegate(WarScriptLanguage script, DefinitionScope scope)
                {
                    MathLibrary.Register(script, scope);
                });
            
            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script '{resourceName}' raised an unhandled exception");
            Assert.IsEmpty(output,
                $"Script '{resourceName}' produced unexpected output:\n{string.Join("\n", output)}");
        }

        // ── Assert-only scripts (empty output = all asserts passed) ──

        [Test]
        public void IsSameTree()
        {
            RunAssertOnlyScript("is_same_tree.ws");
        }

        [Test]
        public void BinarySearch()
        {
            RunAssertOnlyScript("binary_search.ws");
        }

        [Test]
        public void BubbleSort()
        {
            RunAssertOnlyScript("bubble_sort.ws");
        }

        [Test]
        public void Stack()
        {
            RunAssertOnlyScript("stack.ws");
        }

        [Test]
        public void InstanceOf()
        {
            RunAssertOnlyScript("instance_of.ws");
        }

        [Test]
        public void CastType()
        {
            RunAssertOnlyScript("cast_type.ws");
        }

        [Test]
        public void Calculator()
        {
            RunAssertOnlyScript("calculator.ws");
        }

        // Scripts with expected output

        [Test]
        public void RaiseException()
        {
            var (_, output) = TestHelper.RunFile("raise_exception.ws");

            // The script has an unhandled exception — the logger receives:
            // 1. "Do something useful ..." (from print in do_something)
            // 2. The full exception + stack trace (from ExceptionContext.PrintStackTrace)
            Assert.AreEqual(2, output.Count, $"Unexpected output:\n{string.Join("\n", output)}");
            Assert.AreEqual("Do something useful ...", output[0]);

            // The exception printout includes the class ToString and the stack trace.
            // It arrives as a single logger call from PrintStackTrace.
            var exceptionOutput = output[1];
            Assert.That(exceptionOutput, Does.Contain("WarScript.Context.Definition.ClassDefinition"));
            Assert.That(exceptionOutput, Does.Contain("at do_something_else:"));
            Assert.That(exceptionOutput, Does.Contain("at perform_business_operation:"));
            Assert.That(exceptionOutput, Does.Contain("at raise_exception.ws:"));
        }

        [Test]
        public void HandleException()
        {
            var (_, output) = TestHelper.RunFile("handle_exception.ws");

            // The script handles the exception in rescue + ensure blocks
            Assert.AreEqual(3, output.Count, $"Unexpected output:\n{string.Join("\n", output)}");
            Assert.AreEqual("Do something useful ...", output[0]);
            Assert.AreEqual("Rescuing 'A message that describes the error.'", output[1]);
            Assert.AreEqual("Ensure block", output[2]);
        }
        
        [Test]
        public void ClassCreation()
        {
            // Covers: repeated instantiation, inheritance chains, property isolation, methods across instances, casting
            var (script, output) = TestHelper.RunFile("class_creation.ws");

            Assert.IsFalse(script.ExceptionContext.IsRaised(), $"Script raised an unhandled exception. Output:\n{string.Join("\n", output)}");
            Assert.AreEqual(new[] { "all class tests passed" }, output);
        }
    }
}