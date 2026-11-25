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
    /// print v.magnitude()
    /// </code>
    ///
    /// Imports are static (T3.7, 5.8): <see cref="WarScriptLanguage.Compile"/>
    /// (or <see cref="WarScriptLanguage.LoadBytecode"/>, from the module
    /// header) resolves and compiles the imported file before the importer,
    /// and its functions and classes join the script's one namespace. The
    /// statement compiles to nothing: <see cref="WarScriptLanguage.Run"/> runs
    /// the imports' top-level code before the script's, dependencies first.
    ///
    /// <list type="bullet">
    /// <item>An import cycle is a compile error.</item>
    /// <item>Each file's top-level code runs once per instance.</item>
    /// <item>When the host supplies a <see cref="WarScriptLanguage.BytecodeResolver"/>
    ///       that has precompiled bytecode for the path, that is loaded
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
