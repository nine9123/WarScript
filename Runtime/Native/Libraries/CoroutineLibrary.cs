using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public class CoroutineLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("start_coroutine", new List<string> { "name", "args" }),
                args =>
                {
                    var name = NativeHelper.TextArg(args, 0);
                    var fnArgs = args.Count > 1 && args[1].IsArray
                        ? args[1].ArrayValue.ToArray()
                        : System.Array.Empty<WarValue>();
                    return WarValue.FromNumeric(script.StartCoroutine(name, fnArgs, false));
                },
                "Starts a coroutine. Returns coroutine ID.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("start_coroutine_loop", new List<string> { "name", "args" }),
                args =>
                {
                    var name = NativeHelper.TextArg(args, 0);
                    var fnArgs = args.Count > 1 && args[1].IsArray
                        ? args[1].ArrayValue.ToArray()
                        : System.Array.Empty<WarValue>();
                    return WarValue.FromNumeric(script.StartCoroutine(name, fnArgs, true));
                },
                "Starts a looping coroutine. Returns coroutine ID.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("stop_coroutine", new List<string> { "id" }),
                args =>
                {
                    script.StopCoroutine(NativeHelper.IntArg(args, 0));
                    return WarValue.Null;
                },
                "Stops a coroutine by ID.", "null"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("stop_all_coroutines", new List<string>()),
                args => { script.StopAllCoroutines(); return WarValue.Null; },
                "Stops all active coroutines.", "null"));
        }
    }
}
