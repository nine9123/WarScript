#nullable enable

namespace WarScript.Bytecode
{
    /// <summary>
    /// A function value (a lambda), as a script sees it: the
    /// <see cref="WarScript.Expression.Value.WarValue"/> is a
    /// <c>NativeObject</c> holding one of these. It pairs the compiled code
    /// with the instance that owns it (plan 6.7, Q-1, T3.5): calling a
    /// function value owned by another instance raises TypeError "belongs to
    /// another script instance".
    ///
    /// The compiler and the bytecode loader create one per lambda constant of
    /// the instance they compile or load for, so evaluating a lambda literal
    /// allocates nothing and evaluates to the same value each time (equality
    /// is by reference, as before). Only the <see cref="Function"/> is written
    /// to a bytecode file.
    /// </summary>
    public sealed class FunctionValue
    {
        /// <summary>The compiled code.</summary>
        public CompiledFunction Function { get; }

        /// <summary>
        /// The instance whose code this is; null means no owner (callable
        /// from any instance).
        /// </summary>
        public WarScriptLanguage? Owner { get; }

        /// <summary>The function's name (<c>&lt;lambda&gt;</c> for a lambda).</summary>
        public string Name => Function.Name;

        public FunctionValue(CompiledFunction function, WarScriptLanguage? owner)
        {
            Function = function;
            Owner = owner;
        }
    }
}
