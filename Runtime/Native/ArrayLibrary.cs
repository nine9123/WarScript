using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    public static class ArrayLibrary
    {
        public static void Register(WarScriptLanguage script, DefinitionScope scope)
        {
            // arr_remove_at [array, index] — removes element at index, returns it
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_remove_at", new List<string> { "arr", "index" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var index = (int)NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    var list = arr.GetValue();
                    
                    if (index < 0 || index >= list.Count)
                        return script.Null;
                        
                    var removed = list[index];
                    list.RemoveAt(index);
                    return removed;
                },
                "Removes element at index, returns it.",
                "IValue"));

            // arr_remove [array, value] — removes first occurrence, returns true/false
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_remove", new List<string> { "arr", "value" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var value = args[1];
                    var list = arr.GetValue();

                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i] == null) continue;

                        var match = list[i].Equals(value);
                        if (match)
                        {
                            list.RemoveAt(i);
                            return new LogicalValue(script, true);
                        }
                    }
                    return new LogicalValue(script, false);
                },
                "Removes first occurrence of value. Returns true if found.",
                "LogicalValue"));

            // arr_length [array] — returns count
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_length", new List<string> { "arr" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    return new NumericValue(script, arr.GetValue().Count);
                },
                "Returns array length.", "NumericValue"));

            // arr_contains [array, value] — returns true/false
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_contains", new List<string> { "arr", "value" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var value = args[1];
                    var list = arr.GetValue();
                    
                    foreach (var item in list)
                    {
                        if (item != null && item.Equals(value))
                            return new LogicalValue(script, true);
                    }
                    return new LogicalValue(script, false);
                },
                "Returns true if array contains value.", "LogicalValue"));

            // arr_index_of [array, value] — returns index or -1
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_index_of", new List<string> { "arr", "value" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var value = args[1];
                    var list = arr.GetValue();
                    
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i] != null && list[i].Equals(value))
                            return new NumericValue(script, i);
                    }
                    return new NumericValue(script, -1);
                },
                "Returns index of first occurrence, or -1.",
                "NumericValue"));

            // arr_clear [array] — empties the array
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_clear", new List<string> { "arr" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    arr.GetValue().Clear();
                    return script.Null;
                },
                "Removes all elements.", "null"));

            // arr_pop [array] — removes and returns last element
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_pop", new List<string> { "arr" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var list = arr.GetValue();
                    
                    if (list.Count == 0)
                        return script.Null;
                        
                    var last = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                    return last;
                },
                "Removes and returns last element.",
                "IValue"));

            // arr_insert [array, index, value] — inserts at position
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_insert", new List<string> { "arr", "index", "value" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    var index = (int)NativeHelper.Arg<NumericValue>(args, 1).GetValue();
                    var value = args[2];
                    var list = arr.GetValue();
                    
                    if (index < 0) index = 0;
                    if (index > list.Count) index = list.Count;
                    
                    list.Insert(index, value);
                    return arr;
                },
                "Inserts value at index.",
                "ArrayValue"));
            
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("Array_copy", new List<string> { "arr" }),
                args =>
                {
                    var arr = NativeHelper.Arg<ArrayValue>(args, 0);
                    return new ArrayValue(script, new List<IValue>(arr.GetValue()));
                },
                "Returns a shallow copy of the array.", "ArrayValue"));
        }
    }
}