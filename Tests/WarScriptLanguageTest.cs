using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarScript;

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
        private static string GetResourcePath(
            string resourceName,
            [CallerFilePath] string sourceFilePath = "")
        {
            // sourceFilePath is the absolute path to THIS .cs file at compile time.
            // The resources/ folder sits next to it in the same directory.
            var testDir = Path.GetDirectoryName(sourceFilePath)!;
            var path = Path.Combine(testDir, "resources", resourceName);
            if (File.Exists(path))
                return path;

            // Fallback: relative to working directory (dotnet test from Tests/)
            path = Path.Combine("resources", resourceName);
            if (File.Exists(path))
                return path;

            throw new FileNotFoundException(
                $"Test resource '{resourceName}' not found. " +
                $"Looked in: {Path.Combine(testDir, "resources")}");
        }

        /// <summary>
        /// Runs a .ws script file and captures all logger output
        /// (both print statements and unhandled exception stack traces).
        /// </summary>
        private static (WarScriptLanguage script, List<string> output) RunFile(string resourceName)
        {
            var path = GetResourcePath(resourceName);
            var sourceCode = File.ReadAllText(path);
            var scriptName = Path.GetFileName(path);

            var output = new List<string>();
            var script = new WarScriptLanguage(
                scriptName: scriptName,
                sourceCode: sourceCode,
                setupGlobalScope: _ => { },
                fileResolver: null,
                logger: (s, msg) => output.Add(msg));

            return (script, output);
        }

        /// <summary>
        /// Runs a script that uses only asserts — any output means something
        /// went wrong (an unhandled exception was logged).
        /// </summary>
        private static void RunAssertOnlyScript(string resourceName)
        {
            var (script, output) = RunFile(resourceName);
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
            var (_, output) = RunFile("raise_exception.ws");

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
            var (_, output) = RunFile("handle_exception.ws");

            // The script handles the exception in rescue + ensure blocks
            Assert.AreEqual(3, output.Count, $"Unexpected output:\n{string.Join("\n", output)}");
            Assert.AreEqual("Do something useful ...", output[0]);
            Assert.AreEqual("Rescuing 'A message that describes the error.'", output[1]);
            Assert.AreEqual("Ensure block", output[2]);
        }
    }
}