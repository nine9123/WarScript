#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FixMath;
using WarScript.Bytecode;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript
{
    /// <summary>
    /// What a host provides to its scripts, without the implementations
    /// (PROP-30, T3.8): native functions (name and parameters), host
    /// globals, host constants, host enums (<c>[WsEnum]</c>) and host
    /// classes. <see cref="Register"/> declares them on an instance as
    /// stubs, so the compiler checks a script against the game's natives
    /// where the game itself is not running (the runner's
    /// <c>--manifest</c>, a later <c>wsc</c>). A stub function or method
    /// that is called fails with a rescuable error; a stub global or
    /// constant holds null.
    ///
    /// <para><c>WsBindingGenerator</c> writes the manifest of every
    /// <c>[WsModule]</c>; <see cref="FromScript"/> takes the manifest of
    /// an instance the host has set up (natives registered by hand
    /// included) and <see cref="ToString"/> writes it.</para>
    ///
    /// <para>The text format is one entry per line; blank lines and lines
    /// starting with <c>#</c> are ignored, names are separated by single
    /// spaces, and the first other line is the header
    /// <c>warscript-manifest 2</c> (a version 1 manifest, without constant
    /// types, is read too):</para>
    /// <code>
    /// function &lt;name&gt; &lt;min arity&gt; &lt;parameter&gt;...
    /// global &lt;name&gt;
    /// const &lt;name&gt; [&lt;type&gt;]          (number, logical, text or array; absent: unknown)
    /// enum &lt;name&gt; &lt;member&gt;=&lt;integer&gt;...
    /// class &lt;name&gt; &lt;constructor parameter&gt;...
    /// fields &lt;class&gt; &lt;field&gt;...       (absent: the class's fields are unknown)
    /// method &lt;class&gt; &lt;name&gt; &lt;min arity&gt; &lt;parameter&gt;...
    /// </code>
    /// </summary>
    public sealed class NativeManifest
    {
        /// <summary>The first line of a manifest file (after comments).</summary>
        public const string Header = "warscript-manifest 2";

        /// <summary>The header of a version 1 manifest (no constant types), still read.</summary>
        public const string HeaderV1 = "warscript-manifest 1";

        /// <summary>A native function or method: its name, its parameters, and how many of them are required.</summary>
        public sealed class FunctionEntry
        {
            public string Name { get; }
            public int MinArity { get; }
            public IReadOnlyList<string> Parameters { get; }

            public FunctionEntry(string name, int minArity, IReadOnlyList<string> parameters)
            {
                CheckName(name);
                foreach (var p in parameters) CheckName(p);
                if (minArity < 0 || minArity > parameters.Count)
                    throw new ArgumentOutOfRangeException(nameof(minArity),
                        FormattableString.Invariant($"'{name}': min arity {minArity} is not between 0 and {parameters.Count}"));
                Name = name;
                MinArity = minArity;
                Parameters = parameters.ToArray();
            }
        }

        /// <summary>A host enum: its members and their values.</summary>
        public sealed class EnumEntry
        {
            public string Name { get; }
            public IReadOnlyList<(string Name, int Value)> Members { get; }

            public EnumEntry(string name, IReadOnlyList<(string Name, int Value)> members)
            {
                CheckName(name);
                foreach (var m in members) CheckName(m.Name);
                Name = name;
                Members = members.ToArray();
            }
        }

        /// <summary>
        /// A host class: its constructor parameters, its fields (null when
        /// unknown: the compiler then accepts any <c>obj.x</c>) and its
        /// methods.
        /// </summary>
        public sealed class ClassEntry
        {
            public string Name { get; }
            public IReadOnlyList<string> Parameters { get; }
            public IReadOnlyList<string>? Fields { get; internal set; }
            public List<FunctionEntry> Methods { get; } = new List<FunctionEntry>();

            public ClassEntry(string name, IReadOnlyList<string> parameters, IReadOnlyList<string>? fields = null)
            {
                CheckName(name);
                foreach (var p in parameters) CheckName(p);
                if (fields != null) foreach (var f in fields) CheckName(f);
                Name = name;
                Parameters = parameters.ToArray();
                Fields = fields?.ToArray();
            }
        }

        public List<FunctionEntry> Functions { get; } = new List<FunctionEntry>();
        public List<string> Globals { get; } = new List<string>();
        public List<string> Constants { get; } = new List<string>();

        /// <summary>
        /// The type of a constant's value, by constant name: <c>number</c>,
        /// <c>logical</c>, <c>text</c> or <c>array</c> (a constant without an entry has
        /// an unknown type). The compiler types a read of the stub constant
        /// with it (T3.8, PROP-28), since the stub itself holds null.
        /// </summary>
        public Dictionary<string, string> ConstantTypes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<EnumEntry> Enums { get; } = new List<EnumEntry>();
        public List<ClassEntry> Classes { get; } = new List<ClassEntry>();

        // ── Text ──

        /// <summary>
        /// Reads a manifest written in the format above. Throws
        /// <see cref="FormatException"/> naming the line for anything else
        /// (no header, an unknown entry, a bad count or name, a
        /// <c>fields</c>/<c>method</c> line before its class).
        /// </summary>
        public static NativeManifest Parse(string text)
        {
            var manifest = new NativeManifest();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var headerSeen = false;
            var version = 0;
            var classes = new Dictionary<string, ClassEntry>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var lineNumber = i + 1;
                try
                {
                    if (!headerSeen)
                    {
                        if (line == Header) version = 2;
                        else if (line == HeaderV1) version = 1;
                        else throw new FormatException($"expected the header '{Header}'");
                        headerSeen = true;
                        continue;
                    }
                    var parts = line.Split(' ');
                    foreach (var part in parts)
                        if (part.Length == 0) throw new FormatException("names are separated by single spaces");
                    switch (parts[0])
                    {
                        case "function":
                            manifest.Functions.Add(ParseFunction(parts, 1));
                            break;
                        case "global":
                            Expect(parts, 2);
                            CheckName(parts[1]);
                            manifest.Globals.Add(parts[1]);
                            break;
                        case "const":
                            if (version < 2) Expect(parts, 2);
                            else if (parts.Length != 2 && parts.Length != 3)
                                throw new FormatException("'const' takes a name and an optional type");
                            CheckName(parts[1]);
                            manifest.Constants.Add(parts[1]);
                            if (parts.Length == 3)
                            {
                                if (StaticTypeOf(parts[2]) == Compiler.StaticType.Unknown)
                                    throw new FormatException($"constant type '{parts[2]}' is not number, logical, text or array");
                                manifest.ConstantTypes[parts[1]] = parts[2];
                            }
                            break;
                        case "enum":
                        {
                            if (parts.Length < 2) throw new FormatException("'enum' needs a name");
                            var members = new List<(string, int)>();
                            for (var p = 2; p < parts.Length; p++)
                            {
                                var eq = parts[p].IndexOf('=');
                                if (eq <= 0 || !int.TryParse(parts[p].Substring(eq + 1), NumberStyles.AllowLeadingSign,
                                        CultureInfo.InvariantCulture, out var value))
                                    throw new FormatException($"enum member '{parts[p]}' is not <name>=<integer>");
                                members.Add((parts[p].Substring(0, eq), value));
                            }
                            manifest.Enums.Add(new EnumEntry(parts[1], members));
                            break;
                        }
                        case "class":
                        {
                            if (parts.Length < 2) throw new FormatException("'class' needs a name");
                            var c = new ClassEntry(parts[1], parts.Skip(2).ToArray());
                            if (classes.ContainsKey(c.Name)) throw new FormatException($"class '{c.Name}' is listed twice");
                            classes.Add(c.Name, c);
                            manifest.Classes.Add(c);
                            break;
                        }
                        case "fields":
                        {
                            var c = OwnerClass(parts, classes);
                            var fields = parts.Skip(2).ToArray();
                            foreach (var f in fields) CheckName(f);
                            c.Fields = fields;
                            break;
                        }
                        case "method":
                            OwnerClass(parts, classes).Methods.Add(ParseFunction(parts, 2));
                            break;
                        default:
                            throw new FormatException($"unknown entry '{parts[0]}'");
                    }
                }
                catch (System.Exception e) when (e is FormatException || e is ArgumentException)
                {
                    throw new FormatException(
                        FormattableString.Invariant($"Native manifest, line {lineNumber}: {e.Message}"), e);
                }
            }
            if (!headerSeen) throw new FormatException($"Native manifest: no header '{Header}'");
            return manifest;
        }

        private static void Expect(string[] parts, int count)
        {
            if (parts.Length != count)
                throw new FormatException(FormattableString.Invariant($"'{parts[0]}' takes {count - 1} name(s)"));
        }

        private static FunctionEntry ParseFunction(string[] parts, int at)
        {
            if (parts.Length < at + 2) throw new FormatException($"'{parts[0]}' needs a name and a min arity");
            if (!int.TryParse(parts[at + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var min))
                throw new FormatException($"min arity '{parts[at + 1]}' is not a number");
            return new FunctionEntry(parts[at], min, parts.Skip(at + 2).ToArray());
        }

        private static ClassEntry OwnerClass(string[] parts, Dictionary<string, ClassEntry> classes)
        {
            if (parts.Length < 2) throw new FormatException($"'{parts[0]}' needs a class name");
            if (!classes.TryGetValue(parts[1], out var c))
                throw new FormatException($"'{parts[0]}' names class '{parts[1]}', which no earlier 'class' line declares");
            return c;
        }

        private static void CheckName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a name is empty");
            foreach (var ch in name)
                if (char.IsWhiteSpace(ch) || ch == '=' || ch == '#')
                    throw new ArgumentException($"'{name}' is not a name");
        }

        /// <summary>
        /// The manifest in the text format, header first, each section
        /// sorted by name (ordinal), so the same host writes the same text.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (var f in Functions.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Parameters.Count))
                WriteFunction(sb, "function ", f);
            foreach (var g in Globals.OrderBy(g => g, StringComparer.Ordinal))
                sb.Append("global ").Append(g).Append('\n');
            foreach (var c in Constants.OrderBy(c => c, StringComparer.Ordinal))
            {
                sb.Append("const ").Append(c);
                if (ConstantTypes.TryGetValue(c, out var type)) sb.Append(' ').Append(type);
                sb.Append('\n');
            }
            foreach (var e in Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                sb.Append("enum ").Append(e.Name);
                foreach (var (name, value) in e.Members)
                    sb.Append(' ').Append(name).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            foreach (var c in Classes.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                sb.Append("class ").Append(c.Name);
                foreach (var p in c.Parameters) sb.Append(' ').Append(p);
                sb.Append('\n');
                if (c.Fields != null)
                {
                    sb.Append("fields ").Append(c.Name);
                    foreach (var f in c.Fields.OrderBy(f => f, StringComparer.Ordinal)) sb.Append(' ').Append(f);
                    sb.Append('\n');
                }
                foreach (var m in c.Methods.OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Parameters.Count))
                    WriteFunction(sb, "method " + c.Name + " ", m);
            }
            return sb.ToString();
        }

        private static void WriteFunction(StringBuilder sb, string prefix, FunctionEntry f)
        {
            sb.Append(prefix).Append(f.Name).Append(' ').Append(f.MinArity.ToString(CultureInfo.InvariantCulture));
            foreach (var p in f.Parameters) sb.Append(' ').Append(p);
            sb.Append('\n');
        }

        // ── Capture ──

        /// <summary>
        /// The manifest of what <paramref name="script"/>'s host has set up:
        /// the native functions and classes of its
        /// <see cref="WarScriptLanguage.GlobalDefinitionScope"/> and the host
        /// globals of its <see cref="WarScriptLanguage.GlobalMemoryScope"/>
        /// (a host constant is a host global in the constant names; a host
        /// enum is a host constant holding an instance of the class of its
        /// name). Call it after registering the natives and before compiling.
        /// </summary>
        public static NativeManifest FromScript(WarScriptLanguage script)
        {
            var manifest = new NativeManifest();
            var scope = script.GlobalDefinitionScope;
            foreach (var f in scope.Functions)
                if (f is NativeFunctionDefinition)
                    manifest.Functions.Add(FromDefinition(f));

            var enumClasses = new HashSet<ClassDefinition>();
            foreach (var global in script.GlobalMemoryScope.GetAllLocals())
            {
                var name = global.Key;
                var value = global.Value;
                if (value.IsClass && script.ConstantNames.Contains(name)
                    && value.ClassValue.Definition.ClassDetails.Name == name
                    && TryReadEnum(value.ClassValue, out var members))
                {
                    enumClasses.Add(value.ClassValue.Definition);
                    manifest.Enums.Add(new EnumEntry(name, members));
                }
                else if (script.ConstantNames.Contains(name))
                {
                    manifest.Constants.Add(name);
                    // A manifest's stub holds null: keep the type it declared.
                    var type = TypeName(value.IsNull && script.HostConstantTypes.TryGetValue(name, out var declared)
                        ? declared : Compiler.TypeOfValue(value));
                    if (type != null) manifest.ConstantTypes[name] = type;
                }
                else
                    manifest.Globals.Add(name);
            }

            foreach (var c in scope.ClassDefinitions)
            {
                if (enumClasses.Contains(c)) continue;
                var entry = new ClassEntry(c.ClassDetails.Name, c.ClassDetails.Properties,
                    c.Fields?.OrderBy(f => f, StringComparer.Ordinal).ToArray());
                foreach (var m in c.GetDefinitionScope().Functions)
                    entry.Methods.Add(FromDefinition(m));
                manifest.Classes.Add(entry);
            }
            return manifest;
        }

        /// <summary>The manifest name of a constant's type, or null when it has none there (unknown, null).</summary>
        private static string? TypeName(Compiler.StaticType type) => type switch
        {
            Compiler.StaticType.Number => "number",
            Compiler.StaticType.Logical => "logical",
            Compiler.StaticType.Text => "text",
            Compiler.StaticType.Array => "array",
            _ => null,
        };

        private static Compiler.StaticType StaticTypeOf(string name) => name switch
        {
            "number" => Compiler.StaticType.Number,
            "logical" => Compiler.StaticType.Logical,
            "text" => Compiler.StaticType.Text,
            "array" => Compiler.StaticType.Array,
            _ => Compiler.StaticType.Unknown,
        };

        private static FunctionEntry FromDefinition(FunctionDefinition f) =>
            new FunctionEntry(f.Details.Name, f.Details.MinArity, f.Details.Arguments);

        /// <summary>A host enum's members, read from its instance's <c>names</c> and <c>values</c>.</summary>
        private static bool TryReadEnum(ClassData data, out List<(string Name, int Value)> members)
        {
            members = new List<(string, int)>();
            var fields = data.MemoryScope.GetAllLocals();
            if (!fields.TryGetValue("names", out var names) || !names.IsArray
                || !fields.TryGetValue("values", out var values) || !values.IsArray
                || names.ArrayValue.Count != values.ArrayValue.Count)
                return false;
            for (var i = 0; i < names.ArrayValue.Count; i++)
            {
                var n = names.ArrayValue[i];
                var v = values.ArrayValue[i];
                if (!n.IsText || !v.IsNumeric || v.Numeric != F64.FromInt(F64.RoundToInt(v.Numeric)))
                    return false;
                members.Add((n.TextValue, F64.RoundToInt(v.Numeric)));
            }
            return true;
        }

        // ── Registration ──

        /// <summary>
        /// Declares everything in the manifest on <paramref name="script"/>
        /// that its host has not registered itself (call it after the real
        /// natives, before compiling): a function or method whose name
        /// already has a native with an overlapping arity, a class, global
        /// or constant whose name is taken, is left as it is.
        /// <list type="bullet">
        /// <item>A function or method is a native that raises the rescuable
        /// <c>Native function '&lt;name&gt;' failed: ...</c> when called.</item>
        /// <item>A global or constant is a host global holding null; a
        /// constant also joins the constant names, and its type (if the
        /// manifest gives one) types its reads in the compiler's operand
        /// checks (T3.8, PROP-28).</item>
        /// <item>An enum is a host enum, as <c>[WsEnum]</c> generates it: it
        /// works at run time.</item>
        /// <item>A class is a host class with no constructor body, its
        /// fields (when listed) declared for the compiler's checks.</item>
        /// </list>
        /// </summary>
        public void Register(WarScriptLanguage script)
        {
            var scope = script.GlobalDefinitionScope;
            var globals = script.GlobalMemoryScope;

            foreach (var f in Functions)
                if (!HasOverlap(scope, f))
                    scope.AddFunction(Stub(f, "function"));

            foreach (var name in Globals)
                if (!globals.HasLocal(name))
                    globals.Set(name, WarValue.Null);

            foreach (var name in Constants)
                if (!globals.HasLocal(name))
                {
                    globals.Set(name, WarValue.Null);
                    script.ConstantNames.Add(name);
                    if (ConstantTypes.TryGetValue(name, out var type))
                        script.HostConstantTypes[name] = StaticTypeOf(type);
                }

            foreach (var e in Enums)
                if (!globals.HasLocal(e.Name) && scope.GetOwnClass(e.Name) == null)
                    RegisterEnum(script, scope, e);

            foreach (var c in Classes)
            {
                if (scope.GetOwnClass(c.Name) != null) continue;
                var classScope = new DefinitionScope(script, null);
                foreach (var m in c.Methods)
                    if (!HasOverlap(classScope, m))
                        classScope.AddFunction(Stub(m, "method"));
                var definition = new ClassDefinition(
                    new ClassDetails(c.Name, c.Parameters.ToList()), new List<ClassDetails>(),
                    new ClassStatement(script, 0, c.Name), classScope);
                if (c.Fields != null)
                {
                    // The constructor parameters are fields too (Compiler.OwnFields).
                    definition.Fields = new HashSet<string>(c.Fields, StringComparer.Ordinal);
                    definition.Fields.UnionWith(c.Parameters);
                }
                scope.AddClass(definition);
            }
        }

        private static bool HasOverlap(DefinitionScope scope, FunctionEntry f)
        {
            for (var arity = f.MinArity; arity <= f.Parameters.Count; arity++)
                if (scope.GetFunction(f.Name, arity) != null) return true;
            return false;
        }

        private static NativeFunctionDefinition Stub(FunctionEntry f, string what)
        {
            var name = f.Name;
            return new NativeFunctionDefinition(
                new FunctionDetails(name, f.Parameters.ToList(), f.MinArity < f.Parameters.Count ? f.MinArity : -1),
                _ => throw new InvalidOperationException(
                    $"the {what} '{name}' is only declared by a native manifest: it has no implementation here"),
                "Declared by a native manifest", "WarValue");
        }

        /// <summary>A host enum, built the way <c>WsBindingGenerator</c> generates a <c>[WsEnum]</c>.</summary>
        private static void RegisterEnum(WarScriptLanguage script, DefinitionScope scope, EnumEntry e)
        {
            var enumScope = new DefinitionScope(script, null);
            var definition = new ClassDefinition(new ClassDetails(e.Name, new List<string>()),
                new List<ClassDetails>(), new ClassStatement(script, 0, e.Name), enumScope) { IsEnum = true };
            scope.AddClass(definition);

            var members = e.Members.ToArray();
            enumScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails("name", new List<string> { "value" }),
                args =>
                {
                    var v = args.Count > 0 ? args[0] : WarValue.Null;
                    foreach (var (name, value) in members)
                        if (v.IsNumeric && v.Numeric == F64.FromInt(value)) return WarValue.FromText(name);
                    return WarValue.FromText("unknown");
                },
                "Name of a " + e.Name + " value", "TextValue"));

            script.DefinitionContext.PushScope(scope);
            try
            {
                var data = new ClassData(definition, new MemoryScope(script, null), new Dictionary<string, ClassData>());
                data.Relations[e.Name] = data;
                foreach (var (name, value) in members)
                    data.SetProperty(name, WarValue.FromNumeric(value));
                data.SetProperty("values", WarValue.FromArray(members.Select(m => WarValue.FromNumeric(m.Value)).ToList()));
                data.SetProperty("names", WarValue.FromArray(members.Select(m => WarValue.FromText(m.Name)).ToList()));
                data.SetProperty("count", WarValue.FromNumeric(members.Length));
                script.GlobalMemoryScope.Set(e.Name, WarValue.FromClass(data));
                script.ConstantNames.Add(e.Name);
            }
            finally
            {
                script.DefinitionContext.EndScope();
            }
        }
    }
}
