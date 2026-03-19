using System;
using System.Collections.Generic;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Tests
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
            string source,
            Action<DefinitionScope> setupScope = null,
            Func<string, string> fileResolver = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(
                scriptName: "test",
                sourceCode: source,
                setupGlobalScope: scope => setupScope?.Invoke(scope),
                fileResolver: fileResolver,
                logger: (s, msg) => output.Add(msg));
            return (script, output);
        }

        /// <summary>
        /// Execute source code, then call a named function and return the output
        /// captured during both phases.
        /// Useful for testing the Call() API that game engines use each tick.
        /// </summary>
        public static (WarScriptLanguage script, List<string> output) RunAndCall(
            string source,
            string functionName,
            int argCount = 0,
            IValue[] args = null,
            Action<DefinitionScope> setupScope = null)
        {
            var (script, output) = Run(source, setupScope);
            var fn = script.GetFunction(functionName, argCount);
            if (fn != null)
                script.Call(fn, args ?? Array.Empty<IValue>());
            return (script, output);
        }
    }
}
