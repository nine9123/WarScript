#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WarScript.SourceGen
{
    [Generator(LanguageNames.CSharp)]
    public class WsModuleGenerator : IIncrementalGenerator
    {
        private const string ModuleAttr = "WarScript.Attributes.WsModuleAttribute";
        private const string FunctionAttr = "WarScript.Attributes.WsFunctionAttribute";
        private const string ScriptAttr = "WarScript.Attributes.WsScriptAttribute";
        private const string RawArgsAttr = "WarScript.Attributes.WsRawArgsAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all classes with [WsModule]
            var modules = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    ModuleAttr,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => ExtractModuleInfo(ctx))
                .Where(static m => m is not null);

            context.RegisterSourceOutput(modules.Collect(), GenerateCode!);
        }

        // ────────────────────────────────────────────────────────
        //  Model extraction (runs per syntax change)
        // ────────────────────────────────────────────────────────

        private static ModuleInfo? ExtractModuleInfo(GeneratorAttributeSyntaxContext ctx)
        {
            var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
            var attr = ctx.Attributes.First();

            var moduleName = attr.ConstructorArguments[0].Value as string ?? "";
            var description = "";
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Description")
                    description = named.Value.Value as string ?? "";
            }

            var isStatic = classSymbol.IsStatic;
            var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            // Find [WsScript] field/property
            string? scriptField = null;
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IFieldSymbol field)
                {
                    foreach (var a in field.GetAttributes())
                        if (a.AttributeClass?.ToDisplayString() == ScriptAttr)
                            scriptField = field.Name;
                }
                else if (member is IPropertySymbol prop)
                {
                    foreach (var a in prop.GetAttributes())
                        if (a.AttributeClass?.ToDisplayString() == ScriptAttr)
                            scriptField = prop.Name;
                }
            }

            // Find [WsFunction] methods
            var functions = new List<FunctionInfo>();
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol method) continue;
                var funcAttr = method.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FunctionAttr);
                if (funcAttr == null) continue;

                var wsName = funcAttr.ConstructorArguments.Length > 0
                    ? funcAttr.ConstructorArguments[0].Value as string
                    : null;
                if (string.IsNullOrEmpty(wsName))
                    wsName = ToSnakeCase(method.Name);

                var doc = "";
                var returns = "";
                foreach (var named in funcAttr.NamedArguments)
                {
                    if (named.Key == "Doc") doc = named.Value.Value as string ?? "";
                    if (named.Key == "Returns") returns = named.Value.Value as string ?? "";
                }

                var parameters = new List<ParamInfo>();
                foreach (var p in method.Parameters)
                {
                    var hasRawArgs = p.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == RawArgsAttr);
                    parameters.Add(new ParamInfo
                    {
                        Name = p.Name,
                        TypeName = p.Type.ToDisplayString(),
                        IsRawArgs = hasRawArgs
                    });
                }

                if (string.IsNullOrEmpty(returns))
                    returns = InferReturnsDoc(method.ReturnType.ToDisplayString());

                functions.Add(new FunctionInfo
                {
                    CSharpName = method.Name,
                    WsName = wsName!,
                    IsStatic = method.IsStatic,
                    ReturnType = method.ReturnType.ToDisplayString(),
                    Parameters = parameters,
                    Doc = doc,
                    Returns = returns
                });
            }

            if (functions.Count == 0) return null;

            return new ModuleInfo
            {
                ClassName = classSymbol.Name,
                Namespace = ns,
                ModuleName = moduleName,
                Description = description,
                IsStatic = isStatic,
                ScriptField = scriptField,
                Functions = functions
            };
        }

        // ────────────────────────────────────────────────────────
        //  Code generation
        // ────────────────────────────────────────────────────────

        private static void GenerateCode(SourceProductionContext ctx,
            ImmutableArray<ModuleInfo?> modules)
        {
            foreach (var module in modules)
            {
                if (module == null) continue;
                var source = GenerateModuleSource(module);
                ctx.AddSource($"{module.ClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }

        private static string GenerateModuleSource(ModuleInfo m)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using WarScript;");
            sb.AppendLine("using WarScript.Context.Definition;");
            sb.AppendLine("using WarScript.Expression.Value;");
            sb.AppendLine();

            if (m.Namespace != null)
            {
                sb.AppendLine($"namespace {m.Namespace}");
                sb.AppendLine("{");
            }

            var indent = m.Namespace != null ? "    " : "";

            // Open partial class
            var staticKw = m.IsStatic ? "static " : "";
            sb.AppendLine($"{indent}public {staticKw}partial class {m.ClassName}");
            sb.AppendLine($"{indent}{{");

            // Generate Register method
            sb.AppendLine($"{indent}    public {(m.IsStatic ? "static " : "")}void Register(WarScriptLanguage __script, DefinitionScope __scope)");
            sb.AppendLine($"{indent}    {{");

            // Assign script field if present
            if (m.ScriptField != null)
                sb.AppendLine($"{indent}        {m.ScriptField} = __script;");

            foreach (var f in m.Functions)
            {
                var paramNames = f.Parameters
                    .Where(p => !p.IsRawArgs)
                    .Select(p => $"\"{p.Name}\"");
                var paramList = string.Join(", ", paramNames);
                var argCount = f.Parameters.Count(p => !p.IsRawArgs);

                sb.AppendLine();
                sb.AppendLine($"{indent}        __scope.AddFunction(new NativeFunctionDefinition(");
                sb.AppendLine($"{indent}            new FunctionDetails(\"{f.WsName}\", new List<string> {{ {paramList} }}),");
                sb.AppendLine($"{indent}            (__args) =>");
                sb.AppendLine($"{indent}            {{");

                // Marshal arguments
                int argIdx = 0;
                foreach (var p in f.Parameters)
                {
                    if (p.IsRawArgs)
                    {
                        sb.AppendLine($"{indent}                var {p.Name} = __args;");
                    }
                    else
                    {
                        var marshal = GetArgMarshal(p.TypeName, argIdx);
                        sb.AppendLine($"{indent}                {GetCSharpType(p.TypeName)} {p.Name} = {marshal};");
                        argIdx++;
                    }
                }

                // Call the actual method
                var callArgs = string.Join(", ", f.Parameters.Select(p => p.Name));
                var callPrefix = f.IsStatic ? "" : "this.";

                if (f.ReturnType == "void")
                {
                    sb.AppendLine($"{indent}                {callPrefix}{f.CSharpName}({callArgs});");
                    sb.AppendLine($"{indent}                return WarValue.Null;");
                }
                else
                {
                    var resultMarshal = GetReturnMarshal(f.ReturnType);
                    sb.AppendLine($"{indent}                var __result = {callPrefix}{f.CSharpName}({callArgs});");
                    sb.AppendLine($"{indent}                return {resultMarshal};");
                }

                sb.AppendLine($"{indent}            }},");
                sb.AppendLine($"{indent}            \"{EscapeString(f.Doc)}\",");
                sb.AppendLine($"{indent}            \"{EscapeString(f.Returns)}\"));");
            }

            sb.AppendLine($"{indent}    }}");

            // Generate LibraryInfo property
            sb.AppendLine();
            sb.AppendLine($"{indent}    public {(m.IsStatic ? "static " : "")}WarScriptLibraryRegistry.LibraryInfo LibraryInfo => new WarScriptLibraryRegistry.LibraryInfo");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        Name = \"{EscapeString(m.ModuleName)}\",");
            sb.AppendLine($"{indent}        Description = \"{EscapeString(m.Description)}\",");
            sb.AppendLine($"{indent}        Register = {(m.IsStatic ? "" : "this.")}Register");
            sb.AppendLine($"{indent}    }};");

            sb.AppendLine($"{indent}}}");

            if (m.Namespace != null)
                sb.AppendLine("}");

            return sb.ToString();
        }

        // ────────────────────────────────────────────────────────
        //  Type marshaling helpers
        // ────────────────────────────────────────────────────────

        private static string GetArgMarshal(string typeName, int index)
        {
            return typeName switch
            {
                "double" => $"NativeHelper.NumericArg(__args, {index})",
                "int" => $"(int)NativeHelper.NumericArg(__args, {index})",
                "float" => $"(float)NativeHelper.NumericArg(__args, {index})",
                "string" => $"NativeHelper.TextArg(__args, {index})",
                "bool" => $"(__args[{index}].IsLogical ? __args[{index}].LogicalValue : __args[{index}].IsNumeric && __args[{index}].Numeric != 0)",
                "WarScript.Expression.Value.WarValue" => $"__args[{index}]",
                "System.Collections.Generic.List<WarScript.Expression.Value.WarValue>" =>
                    $"NativeHelper.ArrayArg(__args, {index}).ArrayValue",
                _ => $"NativeHelper.NativeArg<{typeName}>(__args, {index})"
            };
        }

        private static string GetCSharpType(string typeName)
        {
            return typeName switch
            {
                "double" => "double",
                "int" => "int",
                "float" => "float",
                "string" => "string",
                "bool" => "bool",
                "WarScript.Expression.Value.WarValue" => "WarValue",
                "System.Collections.Generic.List<WarScript.Expression.Value.WarValue>" =>
                    "List<WarValue>",
                _ => typeName
            };
        }

        private static string GetReturnMarshal(string returnType)
        {
            return returnType switch
            {
                "double" => "WarValue.FromNumeric(__result)",
                "int" => "WarValue.FromNumeric(__result)",
                "float" => "WarValue.FromNumeric(__result)",
                "string" => "__result != null ? WarValue.FromText(__result) : WarValue.Null",
                "bool" => "WarValue.FromLogical(__result)",
                "WarScript.Expression.Value.WarValue" => "__result",
                _ => "WarValue.FromNativeObject(__result)"
            };
        }

        private static string InferReturnsDoc(string returnType)
        {
            return returnType switch
            {
                "double" or "int" or "float" => "NumericValue",
                "string" => "TextValue",
                "bool" => "LogicalValue",
                "void" => "null",
                "WarScript.Expression.Value.WarValue" => "WarValue",
                _ => returnType
            };
        }

        // ────────────────────────────────────────────────────────
        //  Utilities
        // ────────────────────────────────────────────────────────

        private static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        // ────────────────────────────────────────────────────────
        //  Data models
        // ────────────────────────────────────────────────────────

        private class ModuleInfo
        {
            public string ClassName = "";
            public string? Namespace;
            public string ModuleName = "";
            public string Description = "";
            public bool IsStatic;
            public string? ScriptField;
            public List<FunctionInfo> Functions = new();
        }

        private class FunctionInfo
        {
            public string CSharpName = "";
            public string WsName = "";
            public bool IsStatic;
            public string ReturnType = "";
            public List<ParamInfo> Parameters = new();
            public string Doc = "";
            public string Returns = "";
        }

        private class ParamInfo
        {
            public string Name = "";
            public string TypeName = "";
            public bool IsRawArgs;
        }
    }
}
