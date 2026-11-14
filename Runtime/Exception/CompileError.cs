#nullable enable

namespace WarScript.Exception
{
    /// <summary>
    /// The compiler cannot represent the script in bytecode: a function, a
    /// constant pool, a call or an array literal goes past what the format
    /// can encode (T1.8, DX-2). A <see cref="SyntaxException"/>, so it leaves
    /// <c>Run()</c> the way every other compile error does. The script, line
    /// and column are <see cref="SyntaxException"/>'s.
    /// </summary>
    public class CompileError : SyntaxException
    {
        /// <summary>What went wrong, without the location.</summary>
        public string Reason { get; }

        public CompileError(string reason, string? scriptName, int line, int column = 0)
            : base(Describe(reason, scriptName, line), line, column)
        {
            Reason = reason;
            LocateIn(scriptName, null);
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
