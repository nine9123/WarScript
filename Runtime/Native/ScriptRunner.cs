#nullable enable

using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public class ScriptRunner
    {
        public readonly WarScriptLanguage Script;
    
        public ScriptRunner(string scriptName, string sourceCode)
        {
            Script = new WarScriptLanguage(
                scriptName,
                sourceCode,
                ImportScript,
                LogPrintMessage);

            Run();
        }

        private void Run()
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

        protected virtual void SetupGlobalScope(DefinitionScope scope)
        {
            MathLibrary.Register(Script, scope);
            ArrayLibrary.Register(Script, scope);
            UtilityLibrary.Register(Script, scope);
            CoroutineLibrary.Register(Script, scope);
        }
    }
}
