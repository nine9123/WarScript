using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarScript;
using WarScript.Audit;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Convenience helpers for spinning up a WarScriptLanguage instance in tests.
    /// </summary>
    public static class TestHelper
    {
        /// <summary>
        /// Set by <see cref="AuditSuite"/> when the suite runs with auditing on
        /// (T0.8): every <see cref="Run"/> reports into it, under the test's name.
        /// </summary>
        internal static AuditReport SuiteAudit;

        /// <summary>
        /// Execute source code and capture all print output.
        /// </summary>
        public static (WarScriptLanguage script, List<string> output) Run(
            string scriptName,
            string source,
            Action<WarScriptLanguage, DefinitionScope> setupScope = null,
            Func<string, string> fileResolver = null,
            Func<string, byte[]> bytecodeResolver = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(
                scriptName:scriptName,
                sourceCode: source,
                fileResolver: fileResolver,
                logger: (s, msg) => output.Add(msg),
                bytecodeResolver: bytecodeResolver);

            var audit = SuiteAudit;
            if (audit != null)
            {
                var test = TestContext.CurrentContext.Test;
                var prefix = test.ClassName.Substring(test.ClassName.LastIndexOf('.') + 1) + "." + test.MethodName + ": ";
                script.AuditSink = e => audit.Add(new AuditEvent(e.Kind, prefix + e.Script, e.Line, e.Function, e.Detail));
            }
            
            setupScope?.Invoke(script, script.GlobalDefinitionScope);
            
            var before = Invariants.Capture(script);
            var result = script.Run();
            // Run() returns a compile error (T2.3); tests written against v1
            // expect it thrown as the SyntaxException it was.
            if (result.Error?.CompileException != null)
                throw result.Error.CompileException;
            Invariants.Check(script, before);
            
            return (script, output);
        }
        
        /// <summary>
        /// Runs a .ws script file and captures all logger output
        /// (both print statements and unhandled exception stack traces).
        /// </summary>
        public static (WarScriptLanguage script, List<string> output) RunFile(
            string resourceName,
            Action<WarScriptLanguage, DefinitionScope> setupScope = null,
            Func<string, string> fileResolver = null,
            Func<string, byte[]> bytecodeResolver = null)
        {
            var path = GetResourcePath(resourceName);
            var sourceCode = File.ReadAllText(path);
            var scriptName = Path.GetFileName(path);

            return Run(scriptName, sourceCode, setupScope, fileResolver, bytecodeResolver);
        }
        
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
    }
}
