#nullable enable

using System.Collections.Generic;
using WarScript.Bytecode;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript.Expression
{
    /// <summary>
    /// A lambda (anonymous function) expression.
    /// Parsed from: <c>fun [params] body end</c> in expression position.
    ///
    /// At compile time, the body is compiled into a standalone CompiledFunction
    /// and stored as a NativeObject constant. At runtime, the value is pushed
    /// onto the stack and can be stored in variables or passed as arguments.
    ///
    /// Calling a lambda uses the same syntax as calling a named function:
    /// <c>callback [args]</c>. The VM falls back to variable lookup when
    /// no function definition is found for the name.
    /// </summary>
    public class LambdaExpression : IExpression
    {
        public readonly List<string> Parameters;
        public readonly CompositeStatement Body;
        public readonly int Line;

        private readonly WarScriptLanguage _script;
        private CompiledFunction? _cachedCompiled;

        public LambdaExpression(
            WarScriptLanguage script,
            List<string> parameters,
            CompositeStatement body,
            int line)
        {
            _script = script;
            Parameters = parameters;
            Body = body;
            Line = line;
        }

        /// <summary>
        /// Tree-walk evaluation: compile once and cache, return as a function value.
        /// </summary>
        public WarValue Evaluate()
        {
            _cachedCompiled ??= Bytecode.Compiler.CompileLambda(_script, this);
            return WarValue.FromNativeObject(_cachedCompiled);
        }
    }
}
