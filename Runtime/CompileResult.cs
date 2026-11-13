#nullable enable

using System.Collections.Generic;
using System.Globalization;

namespace WarScript
{
    /// <summary>How serious a <see cref="CompileDiagnostic"/> is.</summary>
    public enum DiagnosticSeverity : byte
    {
        /// <summary>The script cannot run.</summary>
        Error,
        /// <summary>The script compiles, but probably not as meant (compile-time warnings arrive with plan T3.8).</summary>
        Warning,
    }

    /// <summary>One message from <see cref="WarScriptLanguage.Compile"/> (plan 6.1).</summary>
    public sealed class CompileDiagnostic
    {
        public DiagnosticSeverity Severity { get; }

        /// <summary>What went wrong. v1 parser messages may still name the line in the text.</summary>
        public string Message { get; }

        /// <summary>The script (or import path) the diagnostic belongs to.</summary>
        public string ScriptName { get; }

        /// <summary>The source line, or 0 when the parser did not report one (positions for every diagnostic come with T3.1).</summary>
        public int Line { get; }

        /// <summary>The source column; 0 until T3.1.</summary>
        public int Column { get; }

        public CompileDiagnostic(DiagnosticSeverity severity, string message, string scriptName, int line, int column)
        {
            Severity = severity;
            Message = message;
            ScriptName = scriptName;
            Line = line;
            Column = column;
        }

        /// <summary><c>script:line:column: error: message</c>.</summary>
        public override string ToString() =>
            ScriptName + ":" + Line.ToString(CultureInfo.InvariantCulture) + ":" +
            Column.ToString(CultureInfo.InvariantCulture) + ": " +
            (Severity == DiagnosticSeverity.Error ? "error" : "warning") + ": " + Message;
    }

    /// <summary>
    /// The outcome of <see cref="WarScriptLanguage.Compile"/>: whether the
    /// script compiled, and the diagnostics the compiler produced (plan 6.1).
    /// </summary>
    public sealed class CompileResult
    {
        /// <summary>True when the script compiled: it has no error diagnostic.</summary>
        public bool Success { get; }

        public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }

        internal static readonly CompileResult Succeeded =
            new CompileResult(true, System.Array.Empty<CompileDiagnostic>());

        internal CompileResult(bool success, IReadOnlyList<CompileDiagnostic> diagnostics)
        {
            Success = success;
            Diagnostics = diagnostics;
        }
    }
}
