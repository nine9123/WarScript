using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using WarScript;
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
        /// Execute source code and capture all print output.
        /// </summary>
        public static (WarScriptLanguage script, List<string> output) Run(
            string scriptName,
            string source,
            Action<WarScriptLanguage, DefinitionScope> setupScope = null,
            Func<string, string> fileResolver = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(
                scriptName:scriptName,
                sourceCode: source,
                fileResolver: fileResolver,
                logger: (s, msg) => output.Add(msg));
            
            setupScope?.Invoke(script, script.GlobalDefinitionScope);
            
            script.Run();
            
            return (script, output);
        }
        
        /// <summary>
        /// Runs a .ws script file and captures all logger output
        /// (both print statements and unhandled exception stack traces).
        /// </summary>
        public static (WarScriptLanguage script, List<string> output) RunFile(
            string resourceName,
            Action<WarScriptLanguage, DefinitionScope> setupScope = null,
            Func<string, string> fileResolver = null)
        {
            var path = GetResourcePath(resourceName);
            var sourceCode = File.ReadAllText(path);
            var scriptName = Path.GetFileName(path);

            return Run(scriptName, sourceCode, setupScope, fileResolver);
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
