using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class ArrayLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_remove_at", new List<string> { "arr", "index" }),
                args =>
                {
                    var arr = NativeHelper.ArrayArg(args, 0);
                    var index = NativeHelper.IntArg(args, 1);
                    var list = arr.ArrayValue;
                    if (index < 0 || index >= list.Count) return WarValue.Null;
                    var removed = list[index];
                    list.RemoveAt(index);
                    return removed;
                },
                "Removes element at index, returns it.", "IValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_remove", new List<string> { "arr", "value" }),
                args =>
                {
                    var arr = NativeHelper.ArrayArg(args, 0);
                    var value = args[1];
                    var list = arr.ArrayValue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i].Equals(value))
                        {
                            list.RemoveAt(i);
                            return WarValue.True;
                        }
                    }
                    return WarValue.False;
                },
                "Removes first occurrence of value. Returns true if found.", "LogicalValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_length", new List<string> { "arr" }),
                args => WarValue.FromNumeric(NativeHelper.ArrayArg(args, 0).ArrayValue.Count),
                "Returns array length.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_contains", new List<string> { "arr", "value" }),
                args =>
                {
                    var list = NativeHelper.ArrayArg(args, 0).ArrayValue;
                    var value = args[1];
                    foreach (var item in list)
                        if (item.Equals(value)) return WarValue.True;
                    return WarValue.False;
                },
                "Returns true if array contains value.", "LogicalValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_index_of", new List<string> { "arr", "value" }),
                args =>
                {
                    var list = NativeHelper.ArrayArg(args, 0).ArrayValue;
                    var value = args[1];
                    for (var i = 0; i < list.Count; i++)
                        if (list[i].Equals(value)) return WarValue.FromNumeric(i);
                    return WarValue.FromNumeric(-1);
                },
                "Returns index of first occurrence, or -1.", "NumericValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_clear", new List<string> { "arr" }),
                args => { NativeHelper.ArrayArg(args, 0).ArrayValue.Clear(); return WarValue.Null; },
                "Removes all elements.", "null"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_pop", new List<string> { "arr" }),
                args =>
                {
                    var list = NativeHelper.ArrayArg(args, 0).ArrayValue;
                    if (list.Count == 0) return WarValue.Null;
                    var last = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                    return last;
                },
                "Removes and returns last element.", "IValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_insert", new List<string> { "arr", "index", "value" }),
                args =>
                {
                    var arr = NativeHelper.ArrayArg(args, 0);
                    var index = NativeHelper.IntArg(args, 1);
                    var value = args[2];
                    var list = arr.ArrayValue;
                    if (index < 0) index = 0;
                    if (index > list.Count) index = list.Count;
                    list.Insert(index, value);
                    return arr;
                },
                "Inserts value at index.", "ArrayValue"));

            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_copy", new List<string> { "arr" }),
                args => WarValue.FromArray(new List<WarValue>(NativeHelper.ArrayArg(args, 0).ArrayValue)),
                "Returns a shallow copy of the array.", "ArrayValue"));
        }
    }
}
