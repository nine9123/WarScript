#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace WarScript
{
    /// <summary>How a host entry ended (plan 6.1, 6.3).</summary>
    public enum ScriptStatus : byte
    {
        /// <summary>The entry completed; <see cref="ScriptResult.Value"/> is its return value.</summary>
        Ok,
        /// <summary>A script error (any kind but HostFault) escaped the entry.</summary>
        Error,
        /// <summary>The instance was already halted, so nothing ran; <see cref="ScriptResult.Error"/> is the fault that halted it (plan T2.4).</summary>
        Halted,
        /// <summary>An unexpected C# exception occurred inside the VM.</summary>
        HostFault,
    }

    /// <summary>
    /// One frame of a <see cref="ScriptError"/>'s call stack (plan 6.1, T2.5).
    /// </summary>
    public readonly struct ScriptFrame
    {
        /// <summary>
        /// The compiled function: <c>&lt;script&gt;</c> (top-level code),
        /// <c>tick</c>, <c>Unit#ctor</c>, <c>&lt;lambda&gt;</c>. A method is
        /// named by its own name (<c>hit</c>), not yet <c>Unit.hit</c>.
        /// </summary>
        public readonly string Function;
        public readonly string ScriptName;
        public readonly int Line;
        /// <summary>
        /// The column on <see cref="Line"/>, from 1, or 0 when unknown (T3.1):
        /// the column of the statement, or of the operator, call, <c>new</c>
        /// or index on that line, being executed. Code loaded from a bytecode
        /// file has none (format v3, T3.11).
        /// </summary>
        public readonly int Column;

        public ScriptFrame(string function, string scriptName, int line, int column)
        {
            Function = function;
            ScriptName = scriptName;
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// An error that escaped a host entry (plan 6.1): a script error, a
    /// compile error, or a host fault.
    /// </summary>
    public sealed class ScriptError
    {
        public ScriptErrorKind Kind { get; }

        /// <summary>The error's text: the raised value as text, the compile diagnostic's message, or the host exception's message.</summary>
        public string Message { get; }

        /// <summary>The raised value; for errors the runtime raises (and for compile errors and host faults), the message text.</summary>
        public WarValue Value { get; }

        public string ScriptName { get; }

        /// <summary>
        /// <see cref="WarScriptLanguage.InstanceLabel"/> of the instance the
        /// error escaped from, as it was when the error was reported (PROP-4,
        /// T2.8); null when the host set none. Not part of <see cref="ToString"/>:
        /// the runtime's log lines carry it as a prefix instead.
        /// </summary>
        public string? InstanceLabel { get; }

        /// <summary>The line the error was raised at (the innermost frame's), or 0 when unknown: an error raised outside any frame, a host fault, or a compile error without one.</summary>
        public int Line { get; }

        /// <summary>
        /// The column on <see cref="Line"/>, from 1 (a tab counts as one), or
        /// 0 when unknown (T3.1). A runtime error has its innermost frame's
        /// (<see cref="ScriptFrame.Column"/>); code loaded from a bytecode
        /// file has none.
        /// </summary>
        public int Column { get; }

        /// <summary>
        /// The text of the source line the error is on, without its line
        /// break, when the source is known and the error has a column (T3.1):
        /// <see cref="ToString"/> prints it with a caret under the column.
        /// For a runtime error, only when it is in the instance's own script
        /// (not an import). Null otherwise.
        /// </summary>
        public string? SourceLine { get; }

        /// <summary>
        /// Innermost frame first (T2.5): the frames of the VM the error was
        /// raised in, then those of each script it continued in through a
        /// native's callback. Empty for compile errors, host faults and errors
        /// raised outside any frame.
        /// </summary>
        public IReadOnlyList<ScriptFrame> CallStack { get; }

        /// <summary>The C# exception of a <see cref="ScriptErrorKind.HostFault"/>; null otherwise.</summary>
        public System.Exception? HostException { get; }

        /// <summary>
        /// The compile error behind a <see cref="ScriptErrorKind.CompileError"/>
        /// (the script's own, or an imported file's), so tests and v1 callers
        /// can still see the exception v1 threw.
        /// </summary>
        internal SyntaxException? CompileException { get; }

        internal ScriptError(ScriptErrorKind kind, string message, WarValue value, string scriptName,
            string? instanceLabel, int line, int column, System.Exception? hostException = null,
            SyntaxException? compileException = null, IReadOnlyList<ScriptFrame>? callStack = null,
            string? sourceLine = null)
        {
            SourceLine = sourceLine;
            InstanceLabel = instanceLabel;
            Kind = kind;
            Message = message;
            Value = value;
            ScriptName = scriptName;
            Line = line;
            Column = column;
            CallStack = callStack ?? System.Array.Empty<ScriptFrame>();
            HostException = hostException;
            CompileException = compileException;
        }

        /// <summary>
        /// An error the VM raised and no handler caught, with the script,
        /// line and call stack recorded where it was raised (T2.5); an error
        /// raised outside any frame (a refused nested entry the host made
        /// directly) has <paramref name="scriptName"/>, line 0 and no stack.
        /// </summary>
        /// <summary>
        /// A runtime error. <paramref name="source"/> is the source of
        /// <paramref name="scriptName"/>, when known: an error whose innermost
        /// frame is in that script and has a column shows its line (T3.1).
        /// </summary>
        internal static ScriptError FromRaised(Exception.Exception raised, string scriptName, string? instanceLabel, string? source = null)
        {
            var text = raised.Value.IsText ? raised.Value.TextValue : raised.Value.ToString();
            var stack = raised.CallStack;
            var errorScript = raised.ScriptName ?? scriptName;
            var sourceLine = raised.Column > 0 && source != null && errorScript == scriptName
                ? SourceLineOf(source, raised.Line) : null;
            return new ScriptError(raised.Kind, text, raised.Value, errorScript, instanceLabel, raised.Line, raised.Column,
                callStack: stack == null ? null : stack.ToArray(), sourceLine: sourceLine);
        }

        /// <summary>A compile error: of the script itself (<paramref name="diagnostic"/>) or of an imported file.</summary>
        internal static ScriptError FromCompile(SyntaxException e, CompileDiagnostic diagnostic, string? instanceLabel) =>
            new ScriptError(ScriptErrorKind.CompileError, diagnostic.Message, WarValue.FromText(diagnostic.Message),
                diagnostic.ScriptName, instanceLabel, diagnostic.Line, diagnostic.Column, compileException: e,
                sourceLine: diagnostic.Column > 0 && e.SourceText != null ? SourceLineOf(e.SourceText, diagnostic.Line) : null);

        /// <summary>
        /// Line <paramref name="line"/> (from 1) of <paramref name="source"/>
        /// without its line break, or null past the end. Lines are counted as
        /// the lexer counts them: "\r\n", "\n" and "\r" each end one.
        /// </summary>
        internal static string? SourceLineOf(string source, int line)
        {
            if (line < 1) return null;
            var start = 0;
            for (var current = 1; current < line; current++)
            {
                var end = LineEnd(source, start);
                if (end == source.Length) return null;
                start = end + (source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? 2 : 1);
            }
            return source.Substring(start, LineEnd(source, start) - start);
        }

        /// <summary>Index of the first '\r' or '\n' from <paramref name="start"/>, or the length.</summary>
        private static int LineEnd(string source, int start)
        {
            var end = start;
            while (end < source.Length && source[end] != '\r' && source[end] != '\n') end++;
            return end;
        }

        /// <summary>
        /// The line under <see cref="SourceLine"/>: a caret under the column,
        /// indented with the line's own tabs so it lines up where they do.
        /// </summary>
        private static string Caret(string sourceLine, int column)
        {
            var text = new StringBuilder(column);
            for (var i = 0; i < column - 1; i++)
                text.Append(i < sourceLine.Length && sourceLine[i] == '\t' ? '\t' : ' ');
            text.Append('^');
            return text.ToString();
        }

        internal static ScriptError FromHostFault(System.Exception e, string scriptName, string? instanceLabel) =>
            new ScriptError(ScriptErrorKind.HostFault, e.Message, WarValue.FromText(e.Message),
                scriptName, instanceLabel, 0, 0, hostException: e);

        /// <summary>
        /// <c>script:line:column: Kind: message</c>; then, when the source
        /// line is known (<see cref="SourceLine"/>), that line and a caret
        /// under the column (T3.1); then one <c>  at function (script:line:column)</c>
        /// line per frame.
        /// </summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(ScriptName).Append(':')
                .Append(Line.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(Column.ToString(CultureInfo.InvariantCulture)).Append(": ")
                .Append(Kind.ToString()).Append(": ").Append(Message);
            if (SourceLine != null && Column > 0)
                text.Append('\n').Append(SourceLine).Append('\n').Append(Caret(SourceLine, Column));
            foreach (var frame in CallStack)
                text.Append("\n  at ").Append(frame.Function).Append(" (").Append(frame.ScriptName).Append(':')
                    .Append(frame.Line.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(frame.Column.ToString(CultureInfo.InvariantCulture)).Append(')');
            return text.ToString();
        }
    }

    /// <summary>
    /// What a host entry (<see cref="WarScriptLanguage.Run"/>,
    /// <see cref="WarScriptLanguage.Call"/>, <see cref="WarScriptLanguage.StartCoroutine(string, WarValue[], bool, out int)"/>,
    /// <see cref="WarScriptLanguage.TickCoroutines"/>) ended with (plan 6.1, 6.3).
    /// </summary>
    public readonly struct ScriptResult
    {
        public ScriptStatus Status { get; }

        /// <summary>Ok: the return value (<c>Run</c>: null; <c>TickCoroutines</c>: the number of active coroutines). Null otherwise.</summary>
        public WarValue Value { get; }

        /// <summary>Error and HostFault: the error; Halted: the fault that halted the instance. Null for Ok.</summary>
        public ScriptError? Error { get; }

        public bool IsOk => Status == ScriptStatus.Ok;

        private ScriptResult(ScriptStatus status, WarValue value, ScriptError? error)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        internal static ScriptResult Ok(WarValue value) => new ScriptResult(ScriptStatus.Ok, value, null);

        /// <summary>The answer of a halted instance: nothing ran; the error is the fault that halted it (plan 6.3).</summary>
        internal static ScriptResult HaltedBy(ScriptError fault) => new ScriptResult(ScriptStatus.Halted, WarValue.Null, fault);

        /// <summary>An Error result, or a HostFault one for an error of that kind.</summary>
        internal static ScriptResult Failed(ScriptError error) =>
            new ScriptResult(error.Kind == ScriptErrorKind.HostFault ? ScriptStatus.HostFault : ScriptStatus.Error,
                WarValue.Null, error);

        public override string ToString() =>
            Error == null ? Status.ToString() : Status.ToString() + ": " + Error.ToString();
    }
}
