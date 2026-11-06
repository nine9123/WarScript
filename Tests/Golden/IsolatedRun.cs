using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// No NUnit in this file: Headless~/Runner/WarScript.Runner.csproj compiles it
// too, and writes its result with ToJson.
namespace Tests.Golden
{
    /// <summary>
    /// Runs one golden run in a child process through <c>WarScript.Runner</c>
    /// (plan 9.5), and the JSON both sides use for its result:
    /// <c>{"output": [...], "status": "...", "error": {...}, "invariants": "ok"|"&lt;violation&gt;", "hash": null}</c>.
    /// <c>status</c> is <c>ok</c>, <c>error</c> (unhandled script error),
    /// <c>compile-error</c> or <c>host-fault</c> (a C# exception escaped).
    /// <c>hash</c> stays null until the state hash exists (T0.6).
    /// </summary>
    internal static class IsolatedRun
    {
        /// <summary>Plan 9.5: a child that runs longer than this is killed and reported.</summary>
        public const int TimeoutMilliseconds = 10_000;

        /// <summary>Runner exit codes (plan 9.5). Anything else is a crash.</summary>
        public const int ExitCompleted = 0, ExitInvariantViolated = 2, ExitHostFault = 3;

        /// <summary>
        /// The runner's <c>.dll</c>, recorded by the headless test project as
        /// assembly metadata; null when it is missing (inside Unity) or not built.
        /// </summary>
        public static string RunnerPath()
        {
            var path = typeof(IsolatedRun).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "WarScriptRunner")?.Value;
            return path != null && File.Exists(path) ? path : null;
        }

        public static GoldenRun Execute(string runnerPath, GoldenFile file, GoldenMode mode, string culture)
        {
            var info = new ProcessStartInfo
            {
                FileName = DotnetHost(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            // Arguments rather than ArgumentList: this file also compiles in Unity's .NET profile.
            info.Arguments = string.Join(" ",
                Quote(runnerPath), Quote(file.Path),
                "--mode", mode == GoldenMode.Source ? "source" : "bytecode",
                "--culture", culture.Length == 0 ? "invariant" : culture);

            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(TimeoutMilliseconds))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    process.WaitForExit();
                    return new GoldenRun { Crash = $"timed out after {TimeoutMilliseconds} ms" };
                }
                process.WaitForExit();

                var exit = process.ExitCode;
                if (exit != ExitCompleted && exit != ExitInvariantViolated && exit != ExitHostFault)
                    return new GoldenRun { Crash = $"exit code {exit}: {FirstLine(stderr.Result)}" };

                try
                {
                    return FromJson(stdout.Result);
                }
                catch (FormatException e)
                {
                    return new GoldenRun { Crash = $"unreadable runner output ({e.Message}): {FirstLine(stdout.Result)}" };
                }
            }
        }

        private static string DotnetHost()
        {
            // Set by the dotnet CLI for the processes it starts, including test hosts.
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            return string.IsNullOrEmpty(host) ? "dotnet" : host;
        }

        private static string Quote(string argument) => "\"" + argument + "\"";

        private static string FirstLine(string text)
        {
            text = text.Trim();
            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd('\r');
        }

        // ── JSON ─────────────────────────────────────────────────────────

        public static string ToJson(GoldenRun run)
        {
            var sb = new StringBuilder();
            sb.Append("{\"output\": [");
            for (var i = 0; i < run.Output.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                WriteString(sb, run.Output[i]);
            }
            sb.Append("], \"status\": ");

            if (run.EscapedException != null)
            {
                WriteString(sb, "host-fault");
                sb.Append(", \"error\": {\"message\": ");
                WriteString(sb, run.EscapedException);
                sb.Append('}');
            }
            else if (run.CompileError != null)
            {
                WriteString(sb, "compile-error");
                sb.Append(", \"error\": {\"message\": ");
                WriteString(sb, run.CompileError);
                sb.Append('}');
            }
            else if (run.ErrorMessage != null)
            {
                WriteString(sb, "error");
                sb.Append(", \"error\": {\"kind\": ");
                WriteString(sb, GoldenRun.V1Kind(run.ErrorMessage));
                sb.Append(", \"message\": ");
                WriteString(sb, run.ErrorMessage);
                sb.Append('}');
            }
            else
            {
                WriteString(sb, "ok");
                sb.Append(", \"error\": null");
            }

            sb.Append(", \"invariants\": ");
            WriteString(sb, run.InvariantViolation ?? "ok");
            sb.Append(", \"hash\": null}");
            return sb.ToString();
        }

        public static GoldenRun FromJson(string json)
        {
            var reader = new JsonReader(json);
            if (!(reader.ReadDocument() is Dictionary<string, object> root))
                throw new FormatException("not a JSON object");

            var run = new GoldenRun();
            if (root.TryGetValue("output", out var output) && output is List<object> lines)
                run.Output.AddRange(lines.Select(l => (string)l));

            var error = root.TryGetValue("error", out var e) ? e as Dictionary<string, object> : null;
            var message = error != null && error.TryGetValue("message", out var m) ? m as string : null;
            var status = root.TryGetValue("status", out var s) ? s as string : null;
            switch (status)
            {
                case "ok": break;
                case "error": run.ErrorMessage = message ?? ""; break;
                case "compile-error": run.CompileError = message ?? ""; break;
                case "host-fault": run.EscapedException = message ?? ""; break;
                default: throw new FormatException($"unknown status '{status}'");
            }

            var invariants = root.TryGetValue("invariants", out var inv) ? inv as string : null;
            run.InvariantViolation = invariants == null || invariants == "ok" ? null : invariants;
            return run;
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>
        /// Reads the subset of JSON that <see cref="ToJson"/> writes: objects,
        /// arrays, strings, null, true and false. Numbers are kept as their text.
        /// (System.Text.Json is not available to the tests inside Unity.)
        /// </summary>
        private sealed class JsonReader
        {
            private readonly string _text;
            private int _pos;

            public JsonReader(string text) { _text = text; }

            public object ReadDocument()
            {
                var value = ReadValue();
                SkipWhitespace();
                if (_pos != _text.Length) throw Error("trailing characters");
                return value;
            }

            private object ReadValue()
            {
                SkipWhitespace();
                if (_pos >= _text.Length) throw Error("unexpected end");
                switch (_text[_pos])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 'n': Expect("null"); return null;
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    default:
                        var start = _pos;
                        while (_pos < _text.Length && "+-.0123456789eE".IndexOf(_text[_pos]) >= 0) _pos++;
                        if (_pos == start) throw Error("unexpected character");
                        return _text.Substring(start, _pos - start);
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++;
                SkipWhitespace();
                if (Peek() == '}') { _pos++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("expected ':'");
                    _pos++;
                    result[key] = ReadValue();
                    SkipWhitespace();
                    var c = Peek();
                    _pos++;
                    if (c == '}') return result;
                    if (c != ',') throw Error("expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _pos++;
                SkipWhitespace();
                if (Peek() == ']') { _pos++; return result; }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    var c = Peek();
                    _pos++;
                    if (c == ']') return result;
                    if (c != ',') throw Error("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                if (Peek() != '"') throw Error("expected a string");
                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (_pos >= _text.Length) throw Error("unterminated string");
                    var c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (_pos >= _text.Length) throw Error("unterminated escape");
                    var escape = _text[_pos++];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw Error("short \\u escape");
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default: throw Error("unknown escape");
                    }
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) throw Error("expected " + word);
                _pos += word.Length;
            }

            private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
            }

            private FormatException Error(string what) => new FormatException($"{what} at offset {_pos}");
        }
    }
}
