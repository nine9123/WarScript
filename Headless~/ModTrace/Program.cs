using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using FixMath;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Parser;
using WarScript.Token;

namespace WarScript.ModTrace
{
    /// <summary>
    /// Records a mod's scripting behavior headlessly, as a native-call trace
    /// (behavior baseline, T0.9; Documentation~/migration/BASELINE.md).
    ///
    ///   WarScript.ModTrace &lt;mod folder&gt; [--out &lt;folder&gt;]
    ///
    /// A script that only calls natives has no user globals, so its
    /// ComputeStateHash() only sees what it prints. This tool replaces every
    /// native the mod calls with a stub that records the call and returns
    /// true, then runs each entry script of the mod in its own instance:
    ///
    /// - An entry script is a .ws file that no other .ws file of the mod
    ///   imports. Imports resolve against the mod folder.
    /// - After the top level has run, every function the entry script itself
    ///   defines is called once, in ordinal name order, with null arguments
    ///   (on_room_setup[room], bootstrap[client], ...).
    ///
    /// For each entry script it writes &lt;out&gt;/&lt;path&gt;.trace.txt, one line per
    /// event in order: "call: name[args]" for a native call, "log: line" for
    /// what reached the logger (print output and error traces), and
    /// "run: ..." for the steps above. The last line is the instance's
    /// ComputeStateHash(). summary.txt starts with an FNV-1a 64 hash of the
    /// mod's scripts (each relative path and text, in path order) and lists
    /// every entry script with its call count and an FNV-1a 64 hash of its
    /// trace; the same summary goes to stdout. Runs under the invariant culture.
    ///
    /// Stubs are registered before anything is parsed, with the parameter
    /// names the mod uses: a native called with named arguments takes the
    /// union of those names in order of first use, so v1's parse-time
    /// reordering puts each named argument in a fixed slot (null where the
    /// call omits it). That order is the stub's, not the game's real native.
    ///
    /// Exit codes: 0 traced (scripts may have logged errors; they are in the
    /// trace), 1 bad arguments, 3 a C# exception escaped.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string modDir = null, outDir = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
                else if (modDir == null && !args[i].StartsWith("--", StringComparison.Ordinal)) modDir = args[i];
                else { Console.Error.WriteLine("unexpected argument " + args[i]); return Usage(); }
            }
            if (modDir == null || !Directory.Exists(modDir)) return Usage();
            outDir ??= Path.Combine("Headless~", "out", "modtrace");

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            try
            {
                Trace(Path.GetFullPath(modDir), outDir);
                return 0;
            }
            catch (System.Exception e)
            {
                Console.Error.WriteLine(e);
                return 3;
            }
        }

        private static int Usage()
        {
            Console.Error.WriteLine("usage: WarScript.ModTrace <mod folder> [--out <folder>]");
            return 1;
        }

        private static void Trace(string modDir, string outDir)
        {
            var files = Directory.EnumerateFiles(modDir, "*.ws", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(modDir, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            var scans = files.ToDictionary(f => f, f => Scan(File.ReadAllText(Path.Combine(modDir, f))));
            var imported = new HashSet<string>(scans.Values.SelectMany(s => s.Imports), StringComparer.Ordinal);
            var natives = MergeNatives(scans.Values);
            var entries = files.Where(f => !imported.Contains(f)).ToList();

            // Identifies the mod revision: every script's path and text, in path order.
            var sources = Fnv(files.SelectMany(f => new[] { f, File.ReadAllText(Path.Combine(modDir, f)) }));
            var summary = new List<string>
            {
                $"mod: {files.Count} scripts, sources {sources:x16}, {entries.Count} entry scripts, {natives.Count} natives stubbed",
            };

            foreach (var entry in entries)
            {
                var trace = TraceEntry(modDir, entry, scans[entry], natives, out var calls);
                var target = Path.Combine(outDir, entry + ".trace.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllLines(target, trace);
                summary.Add($"{entry}: {calls} calls, trace {Fnv(trace):x16}");
            }

            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, "summary.txt"), summary);
            foreach (var line in summary) Console.WriteLine(line);
        }

        private static List<string> TraceEntry(string modDir, string entry, ScanResult scan,
            Dictionary<string, NativeShape> natives, out int calls)
        {
            LexicalParser.ClearCache();
            var trace = new List<string>();
            var callCount = 0;

            var script = new WarScriptLanguage(
                entry,
                File.ReadAllText(Path.Combine(modDir, entry)),
                path =>
                {
                    var full = Path.Combine(modDir, path);
                    return File.Exists(full) ? File.ReadAllText(full) : null;
                },
                (_, line) => trace.Add("log: " + line));

            foreach (var native in natives.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                var name = native.Name;
                script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                    new FunctionDetails(name, native.Parameters, native.MinArity),
                    callArgs =>
                    {
                        callCount++;
                        trace.Add("call: " + name + "[" + string.Join(", ", callArgs.Select(Format)) + "]");
                        return WarValue.True;
                    },
                    "ModTrace stub", "LogicalValue"));
            }

            trace.Add("run: top level");
            script.Run();

            foreach (var (name, arity) in scan.Functions.OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Arity))
            {
                var function = script.GetFunction(name, arity);
                if (function == null) continue;
                trace.Add($"run: {name}[{arity} null]");
                script.Call(function, Enumerable.Repeat(WarValue.Null, arity).ToArray());
            }

            trace.Add($"state hash: {script.ComputeStateHash():x16}");
            calls = callCount;
            return trace;
        }

        // ── Formatting ──

        private static string Format(WarValue v)
        {
            if (v.IsNull) return "null";
            if (v.IsLogical) return v.LogicalValue ? "true" : "false";
            if (v.IsNumeric) return FormatNumeric(v.Numeric.Raw);
            if (v.IsText) return Quote(v.TextValue);
            if (v.IsArray) return "{" + string.Join(", ", v.ArrayValue.Select(Format)) + "}";
            if (v.IsClass) return "<instance " + v.ClassValue.Definition.ClassDetails.Name + ">";
            return "<native object>";
        }

        /// Exact decimal form of a 32.32 raw value (every one has a finite expansion).
        private static string FormatNumeric(long raw)
        {
            var neg = raw < 0;
            var mag = BigInteger.Abs(new BigInteger(raw));
            var whole = mag >> 32;
            var frac = mag & uint.MaxValue;
            var text = whole.ToString(CultureInfo.InvariantCulture);
            if (!frac.IsZero)
                text += "." + (frac * BigInteger.Pow(5, 32)).ToString(CultureInfo.InvariantCulture).PadLeft(32, '0').TrimEnd('0');
            return neg ? "-" + text : text;
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static ulong Fnv(IEnumerable<string> lines)
        {
            var hash = 14695981039346656037UL;
            foreach (var line in lines)
            {
                foreach (var b in Encoding.UTF8.GetBytes(line + "\n"))
                {
                    hash ^= b;
                    hash *= 1099511628211UL;
                }
            }
            return hash;
        }

        // ── Token scan: imports, defined functions, called natives ──

        private sealed class ScanResult
        {
            public readonly List<string> Imports = new List<string>();
            public readonly List<(string Name, int Arity)> Functions = new List<(string, int)>();
            public readonly List<Call> Calls = new List<Call>();
        }

        private sealed class Call
        {
            public string Name;
            public int Positional;
            public readonly List<string> Named = new List<string>();
        }

        private sealed class NativeShape
        {
            public string Name;
            public List<string> Parameters;
            public int MinArity;
        }

        private static ScanResult Scan(string source)
        {
            var tokens = LexicalParser.Parse(source)
                .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comment && t.Type != TokenType.LineBreak)
                .ToList();
            var result = new ScanResult();

            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Type == TokenType.Keyword && t.Value == "import" && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Text)
                {
                    result.Imports.Add(tokens[i + 1].Value);
                    continue;
                }
                if (t.Type != TokenType.Variable || i + 1 >= tokens.Count || !IsDivider(tokens[i + 1], "["))
                    continue;

                var previous = i > 0 ? tokens[i - 1] : default;
                var call = ReadArguments(tokens, i + 1, out var close);
                if (previous.Type == TokenType.Keyword && previous.Value == "fun")
                    result.Functions.Add((t.Value, call.Positional + call.Named.Count));
                else if (!(previous.Value == "new" || previous.Value == "::"))
                {
                    call.Name = t.Value;
                    result.Calls.Add(call);
                }
                // Nested calls inside the arguments are found by continuing from '['.
            }

            // Names the mod defines itself are not natives.
            var defined = new HashSet<string>(result.Functions.Select(f => f.Name), StringComparer.Ordinal);
            result.Calls.RemoveAll(c => defined.Contains(c.Name));
            return result;
        }

        private static bool IsDivider(Token.Token t, string value) =>
            (t.Type == TokenType.GroupDivider || t.Type == TokenType.Operator) && t.Value == value;

        /// Counts the top-level arguments of the bracket group opening at 'open'.
        private static Call ReadArguments(List<Token.Token> tokens, int open, out int close)
        {
            var call = new Call();
            var depth = 0;
            var argumentHasTokens = false;
            var argumentStart = open + 1;
            for (close = open; close < tokens.Count; close++)
            {
                var t = tokens[close];
                var opens = IsDivider(t, "[") || IsDivider(t, "{") || IsDivider(t, "(");
                var closes = IsDivider(t, "]") || IsDivider(t, "}") || IsDivider(t, ")");
                if (opens) { depth++; if (depth == 1) continue; }
                if (closes) depth--;
                if (depth == 0 || (depth == 1 && IsDivider(t, ",")))
                {
                    if (argumentHasTokens) Count(tokens, argumentStart, call);
                    argumentHasTokens = false;
                    argumentStart = close + 1;
                    if (depth == 0) break;
                    continue;
                }
                argumentHasTokens = true;
            }
            return call;
        }

        private static void Count(List<Token.Token> tokens, int start, Call call)
        {
            if (start + 1 < tokens.Count && tokens[start].Type == TokenType.Variable && IsDivider(tokens[start + 1], ":"))
                call.Named.Add(tokens[start].Value);
            else
                call.Positional++;
        }

        private static Dictionary<string, NativeShape> MergeNatives(IEnumerable<ScanResult> scans)
        {
            var natives = new Dictionary<string, NativeShape>(StringComparer.Ordinal);
            foreach (var group in scans.SelectMany(s => s.Calls).GroupBy(c => c.Name, StringComparer.Ordinal))
            {
                var parameters = new List<string>();
                var names = group.SelectMany(c => c.Named).Distinct(StringComparer.Ordinal).ToList();
                var maxPositional = group.Max(c => c.Positional);
                for (var p = 0; p < maxPositional; p++) parameters.Add("arg" + p.ToString(CultureInfo.InvariantCulture));
                foreach (var n in names) if (!parameters.Contains(n)) parameters.Add(n);

                natives[group.Key] = new NativeShape
                {
                    Name = group.Key,
                    Parameters = parameters,
                    MinArity = Math.Min(parameters.Count, group.Min(c => c.Positional + c.Named.Count)),
                };
            }
            return natives;
        }
    }
}
