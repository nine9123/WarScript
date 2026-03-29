#nullable enable

using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public class ScriptRunner
    {
        public readonly WarScriptLanguage Script;

        protected ScriptRunner(string scriptName, string sourceCode)
        {
            Script = new WarScriptLanguage(
                scriptName,
                sourceCode,
                ImportScript,
                LogPrintMessage);
        }

        public static ScriptRunner Create(string scriptName, string sourceCode)
        {
            var runner = new ScriptRunner(scriptName, sourceCode);
            runner.Initialize();
            return runner;
        }

        protected void Initialize()
        {
            SetupGlobalScope(Script.GlobalDefinitionScope);
            Script.Run();
        }

        protected virtual string? ImportScript(string scriptPath)
        {
            return null;
        }

        protected virtual void LogPrintMessage(WarScriptLanguage script, string message)
        {
        }

        public void CallDynamic(string functionName, params WarValue[] arguments)
        {
            var function = Script.GetFunction(functionName, arguments.Length);
            if (function != null)
            {
                if (function is NativeFunctionDefinition nativeFunction)
                    nativeFunction.NativeBody(new List<WarValue>(arguments));
                else
                    Script.Call(function, arguments);
            }
        }

        public bool HasFunction(string functionName, int args)
        {
            return Script.GetFunction(functionName, args) != null;
        }

        protected virtual void SetupGlobalScope(DefinitionScope scope)
        {
            MathLibrary.Register(Script, scope);
            ArrayLibrary.Register(Script, scope);
            UtilityLibrary.Register(Script, scope);
            CoroutineLibrary.Register(Script, scope);
        }
    }
}