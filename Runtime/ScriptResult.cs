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
    /// One frame of a <see cref="ScriptError"/>'s call stack (plan 6.1).
    /// Call stacks are captured from T2.5 on.
    /// </summary>
    public readonly struct ScriptFrame
    {
        /// <summary><c>&lt;script&gt;</c>, <c>tick</c>, <c>Unit.hit</c>, <c>Unit#ctor</c>, <c>&lt;lambda&gt;</c>.</summary>
        public readonly string Function;
        public readonly string ScriptName;
        public readonly int Line;
        /// <summary>0 until T3.1.</summary>
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

        /// <summary>The instance's label (plan T2.8); null until then.</summary>
        public string? InstanceLabel { get; }

        /// <summary>The line the error was raised at, or 0 when unknown (runtime errors carry their line from T2.5).</summary>
        public int Line { get; }

        /// <summary>0 until T3.1.</summary>
        public int Column { get; }

        /// <summary>Innermost frame first; empty until T2.5 captures it.</summary>
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
            int line, int column, System.Exception? hostException = null, SyntaxException? compileException = null)
        {
            Kind = kind;
            Message = message;
            Value = value;
            ScriptName = scriptName;
            Line = line;
            Column = column;
            CallStack = System.Array.Empty<ScriptFrame>();
            HostException = hostException;
            CompileException = compileException;
        }

        /// <summary>An error the VM raised and no handler caught.</summary>
        internal static ScriptError FromRaised(Exception.Exception raised, string scriptName)
        {
            var text = raised.Value.IsText ? raised.Value.TextValue : raised.Value.ToString();
            return new ScriptError(raised.Kind, text, raised.Value, scriptName, 0, 0);
        }

        /// <summary>A compile error: of the script itself (<paramref name="diagnostic"/>) or of an imported file.</summary>
        internal static ScriptError FromCompile(SyntaxException e, CompileDiagnostic diagnostic) =>
            new ScriptError(ScriptErrorKind.CompileError, diagnostic.Message, WarValue.FromText(diagnostic.Message),
                diagnostic.ScriptName, diagnostic.Line, diagnostic.Column, compileException: e);

        internal static ScriptError FromHostFault(System.Exception e, string scriptName) =>
            new ScriptError(ScriptErrorKind.HostFault, e.Message, WarValue.FromText(e.Message),
                scriptName, 0, 0, hostException: e);

        /// <summary><c>script:line:column: Kind: message</c>, then one <c>  at function (script:line:column)</c> line per frame.</summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(ScriptName).Append(':')
                .Append(Line.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(Column.ToString(CultureInfo.InvariantCulture)).Append(": ")
                .Append(Kind.ToString()).Append(": ").Append(Message);
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
