#nullable enable

using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Statement;

namespace WarScript
{
    /// <summary>
    /// One parsed file (T3.3): its imports, which come before every other
    /// statement (5.8), its top-level statements and its definitions. The
    /// import statements are also the first top-level statements, since
    /// imports still run when reached until static imports (T3.7).
    /// </summary>
    internal sealed class ParsedModule
    {
        public IReadOnlyList<ImportStatement> Imports { get; }

        public CompositeStatement TopLevel { get; }

        /// <summary>The scope the file's functions and classes were added to.</summary>
        public DefinitionScope Definitions { get; }

        public ParsedModule(IReadOnlyList<ImportStatement> imports, CompositeStatement topLevel, DefinitionScope definitions)
        {
            Imports = imports;
            TopLevel = topLevel;
            Definitions = definitions;
        }
    }

    /// <summary>
    /// The kind of body the parser is in (T3.3): what decides where
    /// <c>global</c>, <c>yield</c> and <c>this</c> are allowed. Nested
    /// blocks (<c>if</c>, loops, <c>begin</c>) keep their body's kind.
    /// </summary>
    internal enum ParseBody
    {
        TopLevel,
        Function,
        Method,
        Lambda,
        ClassBody,
    }
}
