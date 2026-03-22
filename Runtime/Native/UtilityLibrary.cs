using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class UtilityLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("is_null", new List<string> { "object" }),
                args => WarValue.FromLogical(args[0].IsNull),
                "Returns true if the object is null",
                "Logical"));
        }
    }
}
