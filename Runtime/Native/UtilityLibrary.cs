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
                new FunctionDetails("is_null",new List<string> { "object" }),
                args =>
                {
                    return new LogicalValue(script, args[0] is NullValue);
                },
                "Returns true if the object is null",
                "Logical"));
        }
    }
}