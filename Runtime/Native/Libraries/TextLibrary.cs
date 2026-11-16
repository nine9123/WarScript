using System;
using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class TextLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            // Replaces v1's text subtraction, which v2 removed (PROP-7, T3.6).
            // The result is never longer than 's', so it needs no length check.
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Text_remove", new List<string> { "s", "sub" }),
                args =>
                {
                    var s = NativeHelper.TextArg(args, 0);
                    var sub = NativeHelper.TextArg(args, 1);
                    return sub.Length == 0 ? args[0] : WarValue.FromText(s.Replace(sub, string.Empty, StringComparison.Ordinal));
                },
                "Removes every occurrence of sub from s. An empty sub returns s unchanged.", "TextValue"));
        }
    }
}
