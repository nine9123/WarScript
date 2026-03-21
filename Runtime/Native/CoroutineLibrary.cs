using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public class CoroutineLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            // start_coroutine ["function_name", arg1, arg2, ...]
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("start_coroutine", new List<string> { "name", "args" }),
                args =>
                {
                    var name = NativeHelper.Arg<TextValue>(args, 0).GetValue();
                    var fnArgs = args.Count > 1 && args[1] is ArrayValue arr
                        ? arr.GetValue().ToArray()
                        : System.Array.Empty<IValue>();
                    var id = script.StartCoroutine(name, fnArgs, false);
                    return script.GetNumeric(id);
                },
                "Starts a coroutine. Returns coroutine ID.", "NumericValue"));

            // start_coroutine_loop ["function_name", arg1, arg2, ...]
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("start_coroutine_loop", new List<string> { "name", "args" }),
                args =>
                {
                    var name = NativeHelper.Arg<TextValue>(args, 0).GetValue();
                    var fnArgs = args.Count > 1 && args[1] is ArrayValue arr
                        ? arr.GetValue().ToArray()
                        : System.Array.Empty<IValue>();
                    var id = script.StartCoroutine(name, fnArgs, true);
                    return script.GetNumeric(id);
                },
                "Starts a looping coroutine. Returns coroutine ID.", "NumericValue"));

            // stop_coroutine [id]
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("stop_coroutine", new List<string> { "id" }),
                args =>
                {
                    var id = (int)NativeHelper.Arg<NumericValue>(args, 0).GetValue();
                    script.StopCoroutine(id);
                    return script.Null;
                },
                "Stops a coroutine by ID.", "null"));

            // stop_all_coroutines []
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("stop_all_coroutines", new List<string>()),
                args =>
                {
                    script.StopAllCoroutines();
                    return script.Null;
                },
                "Stops all active coroutines.", "null"));
        }
    }
}