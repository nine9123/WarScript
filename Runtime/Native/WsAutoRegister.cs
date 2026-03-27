#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using WarScript.Attributes;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace WarScript.Native
{
    /// <summary>
    /// Reflection-based auto-registration for [WsModule] classes.
    /// Scans assemblies at startup, finds all attributed classes and methods,
    /// and registers them as native WarScript functions.
    ///
    /// No DLL, no source generator, no build step — just attributes and one call.
    ///
    /// Usage:
    ///   // Static modules (auto-discovered)
    ///   WsAutoRegister.All(script, scope);
    ///
    ///   // Instance modules (pass instances for types that need game references)
    ///   WsAutoRegister.All(script, scope, new UnitModule(sim), new PlayerModule(sim));
    ///
    ///   // Or register a single module
    ///   WsAutoRegister.Module(script, scope, new UnitModule(sim));
    /// </summary>
    public static class WsAutoRegister
    {
        // Cache: built once per AppDomain, reused across script instances.
        private static List<ModuleRecord>? _staticModules;
        private static readonly object _lock = new();

        /// <summary>
        /// Discover and register all [WsModule] classes.
        /// Static modules are found automatically via assembly scan.
        /// Instance modules must be passed as arguments.
        /// </summary>
        public static void All(WarScriptLanguage script, DefinitionScope scope,
            params object[] instances)
        {
            // Register static modules (discovered once, cached)
            EnsureStaticModulesDiscovered();
            foreach (var mod in _staticModules!)
                RegisterModule(script, scope, mod, null);

            // Register instance modules (passed by caller)
            foreach (var instance in instances)
                Module(script, scope, instance);
        }

        /// <summary>
        /// Register a single instance module. The instance's class must have [WsModule].
        /// </summary>
        public static void Module(WarScriptLanguage script, DefinitionScope scope, object instance)
        {
            var type = instance.GetType();
            var moduleAttr = type.GetCustomAttribute<WsModuleAttribute>();
            if (moduleAttr == null)
                throw new ArgumentException($"{type.Name} is not marked with [WsModule]");

            var record = BuildModuleRecord(type, moduleAttr);
            RegisterModule(script, scope, record, instance);
        }

        /// <summary>
        /// Collect all registered module info for documentation generation.
        /// </summary>
        public static List<WarScriptLibraryRegistry.LibraryInfo> CollectLibraryInfos(
            WarScriptLanguage script, params object[] instances)
        {
            EnsureStaticModulesDiscovered();
            var result = new List<WarScriptLibraryRegistry.LibraryInfo>();

            foreach (var mod in _staticModules!)
            {
                result.Add(new WarScriptLibraryRegistry.LibraryInfo
                {
                    Name = mod.Name,
                    Description = mod.Description,
                    Register = (s, sc) => RegisterModule(s, sc, mod, null)
                });
            }

            foreach (var instance in instances)
            {
                var type = instance.GetType();
                var attr = type.GetCustomAttribute<WsModuleAttribute>();
                if (attr == null) continue;
                var mod = BuildModuleRecord(type, attr);
                var inst = instance; // capture for lambda
                result.Add(new WarScriptLibraryRegistry.LibraryInfo
                {
                    Name = mod.Name,
                    Description = mod.Description,
                    Register = (s, sc) => RegisterModule(s, sc, mod, inst)
                });
            }

            return result;
        }

        // ────────────────────────────────────────────────────────
        //  Discovery (runs once)
        // ────────────────────────────────────────────────────────

        private static void EnsureStaticModulesDiscovered()
        {
            if (_staticModules != null) return;
            lock (_lock)
            {
                if (_staticModules != null) return;
                _staticModules = new List<ModuleRecord>();

                // Scan all loaded assemblies for [WsModule] on static classes
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    // Skip system assemblies for performance
                    var name = assembly.GetName().Name ?? "";
                    if (name.StartsWith("System") || name.StartsWith("Microsoft")
                        || name.StartsWith("Unity") || name.StartsWith("mscorlib")
                        || name.StartsWith("Mono") || name.StartsWith("netstandard"))
                        continue;

                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (!type.IsAbstract || !type.IsSealed) continue; // static classes only
                            var attr = type.GetCustomAttribute<WsModuleAttribute>();
                            if (attr == null) continue;
                            _staticModules.Add(BuildModuleRecord(type, attr));
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that can't be fully loaded
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────
        //  Module record building
        // ────────────────────────────────────────────────────────

        private static ModuleRecord BuildModuleRecord(Type type, WsModuleAttribute attr)
        {
            var functions = new List<FunctionRecord>();
            FieldInfo? scriptField = null;
            PropertyInfo? scriptProp = null;

            // Find [WsScript] field/property
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.GetCustomAttribute<WsScriptAttribute>() != null)
                    scriptField = field;
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<WsScriptAttribute>() != null)
                    scriptProp = prop;
            }

            // Find [WsFunction] methods
            var flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance
                | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(flags))
            {
                var funcAttr = method.GetCustomAttribute<WsFunctionAttribute>();
                if (funcAttr == null) continue;

                var wsName = string.IsNullOrEmpty(funcAttr.Name)
                    ? ToSnakeCase(method.Name)
                    : funcAttr.Name;

                var parameters = method.GetParameters();
                var paramNames = new List<string>();
                int rawArgsIndex = -1;

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].GetCustomAttribute<WsRawArgsAttribute>() != null)
                        rawArgsIndex = i;
                    else
                        paramNames.Add(parameters[i].Name ?? $"arg{i}");
                }

                var returns = string.IsNullOrEmpty(funcAttr.Returns)
                    ? InferReturnsDoc(method.ReturnType)
                    : funcAttr.Returns;

                functions.Add(new FunctionRecord
                {
                    WsName = wsName,
                    Doc = funcAttr.Doc ?? "",
                    Returns = returns,
                    Method = method,
                    Parameters = parameters,
                    ParamNames = paramNames,
                    RawArgsIndex = rawArgsIndex
                });
            }

            return new ModuleRecord
            {
                Name = attr.Name,
                Description = attr.Description ?? "",
                Type = type,
                Functions = functions,
                ScriptField = scriptField,
                ScriptProp = scriptProp
            };
        }

        // ────────────────────────────────────────────────────────
        //  Registration (builds NativeFunctionDefinitions)
        // ────────────────────────────────────────────────────────

        private static void RegisterModule(WarScriptLanguage script, DefinitionScope scope,
            ModuleRecord mod, object? instance)
        {
            // Inject [WsScript] field/property
            if (instance != null)
            {
                mod.ScriptField?.SetValue(instance, script);
                mod.ScriptProp?.SetValue(instance, script);
            }

            foreach (var func in mod.Functions)
            {
                var method = func.Method;
                var parameters = func.Parameters;
                var rawArgsIndex = func.RawArgsIndex;
                var capturedInstance = method.IsStatic ? null : instance;

                // Build the lambda that marshals args and calls the C# method
                Func<List<WarValue>, WarValue> body = (args) =>
                {
                    var callArgs = new object?[parameters.Length];
                    int wsArgIdx = 0;

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (i == rawArgsIndex)
                        {
                            callArgs[i] = args;
                        }
                        else
                        {
                            callArgs[i] = MarshalArg(args, wsArgIdx, parameters[i].ParameterType);
                            wsArgIdx++;
                        }
                    }

                    var result = method.Invoke(capturedInstance, callArgs);
                    return MarshalReturn(result, method.ReturnType);
                };

                scope.AddFunction(new NativeFunctionDefinition(
                    new FunctionDetails(func.WsName, func.ParamNames),
                    body, func.Doc, func.Returns));
            }
        }

        // ────────────────────────────────────────────────────────
        //  Marshaling
        // ────────────────────────────────────────────────────────

        private static object? MarshalArg(List<WarValue> args, int index, Type targetType)
        {
            if (index >= args.Count) return GetDefault(targetType);

            if (targetType == typeof(double))   return NativeHelper.NumericArg(args, index);
            if (targetType == typeof(int))      return (int)NativeHelper.NumericArg(args, index);
            if (targetType == typeof(float))    return (float)NativeHelper.NumericArg(args, index);
            if (targetType == typeof(string))   return NativeHelper.TextArg(args, index);
            if (targetType == typeof(bool))     return args[index].IsLogical ? args[index].LogicalValue
                                                     : args[index].IsNumeric && args[index].Numeric != 0;
            if (targetType == typeof(WarValue)) return args[index];
            if (targetType == typeof(List<WarValue>)) return NativeHelper.ArrayArg(args, index).ArrayValue;

            // NativeObject fallback
            if (args[index].IsNativeObject && args[index].Ref != null
                && targetType.IsAssignableFrom(args[index].Ref.GetType()))
                return args[index].Ref;

            return GetDefault(targetType);
        }

        private static WarValue MarshalReturn(object? result, Type returnType)
        {
            if (returnType == typeof(void))     return WarValue.Null;
            if (result == null)                 return WarValue.Null;
            if (returnType == typeof(double))   return WarValue.FromNumeric((double)result);
            if (returnType == typeof(int))      return WarValue.FromNumeric((int)result);
            if (returnType == typeof(float))    return WarValue.FromNumeric((float)result);
            if (returnType == typeof(string))   return WarValue.FromText((string)result);
            if (returnType == typeof(bool))     return WarValue.FromLogical((bool)result);
            if (returnType == typeof(WarValue)) return (WarValue)result;

            return WarValue.FromNativeObject(result);
        }

        private static object? GetDefault(Type t) =>
            t.IsValueType ? Activator.CreateInstance(t) : null;

        // ────────────────────────────────────────────────────────
        //  Utilities
        // ────────────────────────────────────────────────────────

        private static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string InferReturnsDoc(Type t)
        {
            if (t == typeof(double) || t == typeof(int) || t == typeof(float)) return "NumericValue";
            if (t == typeof(string)) return "TextValue";
            if (t == typeof(bool)) return "LogicalValue";
            if (t == typeof(void)) return "null";
            if (t == typeof(WarValue)) return "WarValue";
            return t.Name;
        }

        // ────────────────────────────────────────────────────────
        //  Internal data structures
        // ────────────────────────────────────────────────────────

        private class ModuleRecord
        {
            public string Name = "";
            public string Description = "";
            public Type Type = null!;
            public List<FunctionRecord> Functions = new();
            public FieldInfo? ScriptField;
            public PropertyInfo? ScriptProp;
        }

        private class FunctionRecord
        {
            public string WsName = "";
            public string Doc = "";
            public string Returns = "";
            public MethodInfo Method = null!;
            public ParameterInfo[] Parameters = null!;
            public List<string> ParamNames = new();
            public int RawArgsIndex = -1;
        }
    }
}
