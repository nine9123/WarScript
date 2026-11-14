#nullable enable

namespace WarScript.Exception
{
    /// <summary>
    /// The source does not lex, parse or compile. Carries where, when known
    /// (T3.1): the script, the line and the column (from 1; 0 when unknown).
    /// </summary>
    public class SyntaxException : WarScriptException
    {
        public SyntaxException(string message) : base(message)
        {
        }

        public SyntaxException(string message, int line, int column) : base(message)
        {
            Line = line;
            Column = column;
        }

        /// <summary>The script (or import path) the error is in, when known.</summary>
        public string? ScriptName { get; private set; }

        /// <summary>The source line, from 1, or 0 when unknown.</summary>
        public int Line { get; private set; }

        /// <summary>The column on <see cref="Line"/>, from 1, or 0 when unknown.</summary>
        public int Column { get; private set; }

        /// <summary>
        /// The source text <see cref="Line"/> refers to, when known: the error
        /// text shows that line with a caret under the column.
        /// </summary>
        internal string? SourceText { get; private set; }

        /// <summary>Set the position, unless one is already known.</summary>
        internal SyntaxException Locate(int line, int column)
        {
            if (Line == 0 && line > 0)
            {
                Line = line;
                Column = column;
            }
            return this;
        }

        /// <summary>Set the script and its source, unless already known.</summary>
        internal SyntaxException LocateIn(string? scriptName, string? sourceText)
        {
            if (ScriptName == null) ScriptName = scriptName;
            if (SourceText == null) SourceText = sourceText;
            return this;
        }
    }
}
