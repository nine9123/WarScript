#nullable enable

using System.Collections.Generic;

namespace WarScript.Statement
{
    /// <summary>
    /// <c>global a, b</c> (T3.3, DEC-5, PROP-10): assignments to these names
    /// in the enclosing function, method, lambda or class body write the
    /// instance globals. The parser accepts it only inside such a body.
    ///
    /// Until the name-resolution pass (T3.4) the compiler emits nothing for
    /// it, so the body keeps v1's scope rules: a write to a global that
    /// already exists reaches it from a function or lambda, and nothing else
    /// changes.
    /// </summary>
    public class GlobalStatement : Statement
    {
        /// <summary>The declared names, in source order.</summary>
        public IReadOnlyList<string> Names { get; }

        public GlobalStatement(WarScriptLanguage script, int rowNumber, string blockName, IReadOnlyList<string> names)
            : base(script, rowNumber, blockName)
        {
            Names = names;
        }
    }
}
