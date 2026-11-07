using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WarScript;
using WarScript.Exception;
using WarScript.Native;
using WarScript.Parser;
using WarScript.Token;

// No NUnit in this file: Headless~/Runner/WarScript.Runner.csproj compiles it
// too, to run '# isolate' goldens in a child process (plan 9.5).
namespace Tests.Golden
{
    internal enum GoldenMode { Source, Bytecode }

    /// <summary>The directives of one golden file (plan section 9.2).</summary>
    internal sealed class GoldenFile
    {
        /// <summary>Default instruction budget, so a runaway golden fails instead of hanging.</summary>
        internal const int DefaultBudget = 10_000_000;

        public string Path;
        public string Name;
        public string Source;

        public readonly List<string> ExpectedOutput = new List<string>();
        public string ExpectErrorKind;
        public string ExpectErrorSubstring;
        public int ExpectErrorLine;               // 0: any line
        public string ExpectCompileError;         // substring
        public readonly List<string> ExpectWarnings = new List<string>();
        public int Budget = DefaultBudget;
        public long MemoryBudget;
        public bool Isolate;
        public string KnownFailing;
        public bool IgnoreOutput;
        public bool NoStdlib;
        public GoldenMode[] Modes = { GoldenMode.Source, GoldenMode.Bytecode };

        public static GoldenFile Load(string root, string relativePath)
        {
            var path = System.IO.Path.Combine(root, relativePath);
            var file = new GoldenFile
            {
                Path = path,
                Name = System.IO.Path.GetFileName(path),
                Source = File.ReadAllText(path)
            };

            foreach (var (text, row, afterCode) in Comments(file.Source))
                file.Apply(text, row, afterCode);

            return file;
        }

        /// <summary>
        /// The comments of the source with their line and whether code precedes
        /// them on that line. Uses the lexer's Comment tokens so a '#' inside a
        /// text literal is never a directive; a file the lexer rejects (an
        /// expected syntax error) falls back to a plain line scan.
        /// </summary>
        private static IEnumerable<(string text, int row, bool afterCode)> Comments(string source)
        {
            List<Token> tokens = null;
            try { tokens = LexicalParser.Parse(source); }
            catch (SyntaxException) { }

            if (tokens == null)
            {
                var lines = source.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var hash = lines[i].IndexOf('#');
                    if (hash < 0) continue;
                    yield return (lines[i].Substring(hash).TrimEnd('\r'), i + 1,
                                  lines[i].Substring(0, hash).Trim().Length > 0);
                }
                yield break;
            }

            var lastCodeRow = -1;
            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Comment:
                        yield return (token.Value, token.RowNumber, lastCodeRow == token.RowNumber);
                        break;
                    case TokenType.Whitespace:
                    case TokenType.LineBreak:
                        break;
                    default:
                        lastCodeRow = token.RowNumber;
                        break;
                }
            }
        }

        private void Apply(string comment, int row, bool afterCode)
        {
            var body = comment.TrimStart('#').TrimStart();
            var colon = body.IndexOf(':');
            var key = (colon < 0 ? body : body.Substring(0, colon)).Trim();
            // The single space after the colon is not part of the value.
            var value = colon < 0 ? "" : body.Substring(colon + 1);
            if (value.StartsWith(" ")) value = value.Substring(1);
            value = value.TrimEnd();

            switch (key)
            {
                case "expect":
                    if (colon < 0) return;
                    ExpectedOutput.Add(value);
                    break;
                case "expect-error":
                {
                    var kindEnd = value.IndexOf(':');
                    ExpectErrorKind = (kindEnd < 0 ? value : value.Substring(0, kindEnd)).Trim();
                    ExpectErrorSubstring = kindEnd < 0 ? null : value.Substring(kindEnd + 1).Trim();
                    ExpectErrorLine = afterCode ? row : 0;
                    break;
                }
                case "expect-compile-error":
                    ExpectCompileError = value;
                    break;
                case "expect-warning":
                    ExpectWarnings.Add(value);
                    break;
                case "budget":
                    Budget = int.Parse(value.Replace("_", ""), CultureInfo.InvariantCulture);
                    break;
                case "memory-budget":
                    MemoryBudget = long.Parse(value.Replace("_", ""), CultureInfo.InvariantCulture);
                    break;
                case "isolate":
                    Isolate = true;
                    break;
                case "known-failing":
                    KnownFailing = value.Length == 0 ? "?" : value;
                    break;
                case "ignore-output":
                    IgnoreOutput = true;
                    break;
                case "no-stdlib":
                    NoStdlib = true;
                    break;
                case "modes":
                    Modes = value.Split(',')
                        .Select(m => (GoldenMode)Enum.Parse(typeof(GoldenMode), m.Trim(), ignoreCase: true))
                        .ToArray();
                    break;
                // Anything else is an ordinary comment.
            }
        }
    }

    /// <summary>The observable outcome of running a golden file once.</summary>
    internal sealed class GoldenRun
    {
        public readonly List<string> Output = new List<string>();
        public string ErrorMessage;           // unhandled script error, or null
        public string CompileError;           // SyntaxException message, or null
        public string InvariantViolation;
        public string EscapedException;       // "<type>: <message>" of a C# exception that left the runtime
        public string Crash;                  // how an isolated child process died, or null
        public string Hash;                   // ComputeStateHash() as 16 hex digits, once the script ran

        /// <summary>
        /// Runs the file once with <c>CurrentCulture</c> and <c>CurrentUICulture</c>
        /// set to <paramref name="culture"/> ("" for the invariant culture).
        /// </summary>
        /// <summary>
        /// Set when the suite runs with auditing on (T0.8, <c>AuditSuite</c>):
        /// the source-mode, invariant-culture run of every golden reports
        /// into it. The other runs execute the same code, so they would only
        /// multiply the counts.
        /// </summary>
        internal static WarScript.Audit.AuditReport Audit { get; set; }

        public static GoldenRun Execute(GoldenFile file, GoldenMode mode, string culture)
        {
            var savedCulture = CultureInfo.CurrentCulture;
            var savedUICulture = CultureInfo.CurrentUICulture;
            try
            {
                var info = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                return Execute(file, mode, culture.Length == 0 && mode == GoldenMode.Source ? Audit : null);
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
                CultureInfo.CurrentUICulture = savedUICulture;
            }
        }

        public static GoldenRun Execute(GoldenFile file, GoldenMode mode, WarScript.Audit.AuditReport audit = null)
        {
            var run = new GoldenRun();
            var directory = System.IO.Path.GetDirectoryName(file.Path);

            WarScriptLanguage NewInstance(string source)
            {
                var script = new WarScriptLanguage(
                    scriptName: file.Name,
                    sourceCode: source,
                    fileResolver: ImportResolver(directory),
                    logger: (s, msg) => run.Output.Add(msg));
                if (!file.NoStdlib)
                    WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
                script.InstructionBudget = file.Budget;
                script.MemoryBudget = file.MemoryBudget;
                if (audit != null)
                {
                    // The file's path from Golden~ on; imports are named relative to its folder.
                    var label = file.Path.Replace('\\', '/');
                    var at = label.LastIndexOf("/Golden~/", StringComparison.Ordinal);
                    label = at >= 0 ? label.Substring(at + 1) : file.Name;
                    var folder = label.Substring(0, label.Length - file.Name.Length);
                    script.AuditSink = e => audit.Add(new WarScript.Audit.AuditEvent(
                        e.Kind, e.Script == file.Name ? label : folder + e.Script, e.Line, e.Function, e.Detail));
                }
                return script;
            }

            WarScriptLanguage target;
            try
            {
                if (mode == GoldenMode.Source)
                {
                    target = NewInstance(file.Source);
                }
                else
                {
                    var compiler = NewInstance(file.Source);
                    var bytes = new MemoryStream();
                    compiler.SaveBytecode(bytes);
                    target = NewInstance("");
                    bytes.Position = 0;
                    target.LoadBytecode(bytes);
                }
            }
            catch (SyntaxException e)
            {
                run.CompileError = e.Message;
                return run;
            }
            catch (System.Exception e)
            {
                run.EscapedException = Escaped(e);
                return run;
            }

            var before = Invariants.Capture(target);
            try
            {
                target.Run();
            }
            catch (SyntaxException e)
            {
                // v1 compiles lazily inside Run().
                run.CompileError = e.Message;
                return run;
            }
            catch (System.Exception e)
            {
                run.EscapedException = Escaped(e);
                return run;
            }

            // v1 logs an unhandled error through the same logger as print and
            // clears it; take its trace back out of the printed output.
            var unhandled = target.ExceptionContext.LastUnhandled;
            if (unhandled != null)
            {
                run.ErrorMessage = unhandled.Value.ToString();
                var trace = unhandled.ToString();
                var at = run.Output.LastIndexOf(trace);
                if (at >= 0) run.Output.RemoveAt(at);
            }

            run.InvariantViolation = Invariants.Violation(target, before);
            run.Hash = target.ComputeStateHash().ToString("x16", CultureInfo.InvariantCulture);
            return run;
        }

        /// <summary>"&lt;type&gt;: &lt;message&gt; (in &lt;the WarScript method that threw, or called the BCL method that did&gt;)".</summary>
        private static string Escaped(System.Exception e)
        {
            var text = e.GetType().Name + ": " + e.Message;
            var frames = new System.Diagnostics.StackTrace(e).GetFrames();
            if (frames == null) return text;
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                if (type?.Namespace != null && type.Namespace.StartsWith("WarScript", StringComparison.Ordinal))
                    return text + " (in " + type.FullName + "." + method.Name + ")";
            }
            return text;
        }

        internal static string Diff(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
            {
                var e = i < expected.Count ? expected[i] : "<missing>";
                var a = i < actual.Count ? actual[i] : "<missing>";
                if (e == a) continue;
                sb.Append("  line ").Append(i + 1).Append(": expected '").Append(e)
                  .Append("', got '").Append(a).Append("'\n");
            }
            sb.Append("  full output:\n");
            foreach (var line in actual)
                sb.Append("    ").Append(line).Append('\n');
            return sb.ToString();
        }

        private static Func<string, string> ImportResolver(string directory)
        {
            return path =>
            {
                var candidate = System.IO.Path.Combine(directory, path);
                if (!candidate.EndsWith(".ws", StringComparison.Ordinal)) candidate += ".ws";
                return File.Exists(candidate) ? File.ReadAllText(candidate) : null;
            };
        }

        /// <summary>Returns a description of the first unmet expectation, or null.</summary>
        public string CheckExpectations(GoldenFile file)
        {
            if (Crash != null)
                return "child process crashed: " + Crash;

            if (EscapedException != null)
                return "C# exception escaped: " + EscapedException;

            if (file.ExpectCompileError != null)
            {
                if (CompileError == null)
                    return $"expected a compile error containing '{file.ExpectCompileError}', but the script compiled";
                if (!CompileError.Contains(file.ExpectCompileError))
                    return $"expected a compile error containing '{file.ExpectCompileError}', got: {CompileError}";
                return null;
            }
            if (CompileError != null)
                return "compile error: " + CompileError;

            if (file.ExpectWarnings.Count > 0)
                return "'# expect-warning' needs compile-time diagnostics, which arrive with T3.8";

            if (file.ExpectErrorKind != null)
            {
                if (ErrorMessage == null)
                    return $"expected an unhandled {file.ExpectErrorKind}, but the script completed";
                var failure = MatchError(file);
                if (failure != null) return failure;
            }
            else if (ErrorMessage != null)
            {
                return "unhandled error: " + ErrorMessage;
            }

            if (InvariantViolation != null)
                return "invariant violated: " + InvariantViolation;

            if (!file.IgnoreOutput && !file.ExpectedOutput.SequenceEqual(Output))
                return "output differs from the '# expect:' lines:\n" + Diff(file.ExpectedOutput, Output);

            return null;
        }

        private string MatchError(GoldenFile file)
        {
            // v1 errors carry only a message. Until structured errors (T2.5),
            // the kind is recognised from the messages that identify one; any
            // other message is accepted for any expected kind, so goldens can be
            // written with their v2 kind now. Lines are not checked until T2.5
            // either: v1 errors raised by the VM carry no line.
            var kind = V1Kind(ErrorMessage);
            if (kind != null && kind != file.ExpectErrorKind)
                return $"expected {file.ExpectErrorKind}, got {kind}: {ErrorMessage}";
            if (file.ExpectErrorSubstring != null && !ErrorMessage.Contains(file.ExpectErrorSubstring))
                return $"expected an error containing '{file.ExpectErrorSubstring}', got: {ErrorMessage}";
            return null;
        }

        internal static string V1Kind(string message)
        {
            if (message.StartsWith("Division by zero") || message.StartsWith("Modulo by zero"))
                return "DivisionByZero";
            if (message.StartsWith("Instruction budget exceeded"))
                return "InstructionBudgetExceeded";
            if (message.StartsWith("Memory budget exceeded"))
                return "MemoryBudgetExceeded";
            if (message.StartsWith("Assertion error"))
                return "AssertionFailed";
            if (message == "Stack overflow" || message == "Value stack overflow" ||
                message.StartsWith("Exception handler stack overflow"))
                return "LimitExceeded";
            return null;
        }
    }
}
