#nullable enable

namespace WarScript.Exception
{
    /// <summary>
    /// The compiler cannot represent the script in bytecode: a function, a
    /// constant pool, a call or an array literal goes past what the format
    /// can encode (T1.8, DX-2). A <see cref="SyntaxException"/>, so it leaves
    /// <c>Run()</c> the way every other compile error does.
    /// </summary>
    public class CompileError : SyntaxException
    {
        /// <summary>What went wrong, without the location.</summary>
        public string Reason { get; }

        /// <summary>The script (or import path) being compiled, when known.</summary>
        public string? ScriptName { get; }

        /// <summary>The source line, or 0 when unknown.</summary>
        public int Line { get; }

        public CompileError(string reason, string? scriptName, int line)
            : base(Describe(reason, scriptName, line))
        {
            Reason = reason;
            ScriptName = scriptName;
            Line = line;
        }

        private static string Describe(string reason, string? scriptName, int line)
        {
            var text = reason;
            if (line > 0) text += " at line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(scriptName)) text += " in '" + scriptName + "'";
            return text;
        }
    }
}
