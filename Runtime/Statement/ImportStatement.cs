#nullable enable

using WarScript.Bytecode;

namespace WarScript.Statement
{
    /// <summary>
    /// Imports function and class definitions from another script file.
    ///
    /// <code>
    /// import "lib/vectors.ws"
    ///
    /// v = new Vector2[3, 4]
    /// print v::magnitude[]
    /// </code>
    ///
    /// The imported file is lexed, parsed, and its top-level code is executed
    /// in an isolated scope. All function and class definitions produced by
    /// that file are then copied into the caller's current definition scope.
    ///
    /// <list type="bullet">
    /// <item>Circular imports are detected and raise an exception.</item>
    /// <item>Each file path is only imported once per script; subsequent
    ///       imports of the same path reuse the cached definitions.</item>
    /// <item>When the host supplies a <see cref="WarScriptLanguage.BytecodeResolver"/>
    ///       that has precompiled bytecode for the path, that is loaded and run
    ///       instead — the lexer, parser and compiler never see the file.</item>
    /// </list>
    ///
    /// <see cref="StatementParser"/>
    /// <see cref="WarScriptLanguage"/>
    /// </summary>
    public class ImportStatement : Statement
    {
        private readonly string _path;

        // Compiler accessor
        internal string Path => _path;
        
        public ImportStatement(WarScriptLanguage script, int rowNumber, string blockName, string path) 
            : base(script, rowNumber, blockName)
        {
            _path = path;
        }
    }
}
