namespace WarScript.Bytecode
{
    /// <summary>
    /// A compiled function: its bytecode chunk, name, arity, and how many
    /// local variable slots its frame requires.
    /// </summary>
    public class CompiledFunction
    {
        public readonly Chunk Chunk;
        public readonly string Name;
        public readonly int Arity;

        /// <summary>
        /// Total local slots needed (including parameters).
        /// Set by the compiler after compilation is complete.
        /// </summary>
        public int LocalCount;

        /// <summary>
        /// The largest stack height the function reaches, relative to its
        /// frame's stack base, arguments included. Computed by
        /// <see cref="BytecodeVerifier"/> (T1.7); the VM checks it whenever it
        /// pushes a frame, so execution never runs past the value stack.
        /// </summary>
        internal int MaxStack;

        /// <summary>
        /// Debug info: variable name for each local slot (index = slot number).
        /// Parameters occupy slots 0..Arity-1. Hidden locals (like $limit, $step)
        /// are included but prefixed with $. Null entries mean the slot was
        /// only used transiently and has no meaningful name.
        /// </summary>
        public string[] LocalNames;

        public CompiledFunction(string name, int arity)
        {
            Name = name;
            Arity = arity;
            Chunk = new Chunk();
            LocalNames = System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// The kind of a <see cref="CallFrame"/>: what it pushed on the context
    /// stacks (MemoryContext, DefinitionContext, ClassInstanceContext) when
    /// it was entered.
    /// </summary>
    public enum FrameKind : byte
    {
        /// <summary>An entry frame (script body, host call, coroutine): pushes nothing.</summary>
        Script,
        /// <summary>A function or lambda call: pushes a function-local MemoryScope.</summary>
        Function,
        /// <summary>A method call: pushes the class DefinitionScope, the instance's MemoryScope and ClassInstanceContext entry, and a method-local MemoryScope.</summary>
        Method,
        /// <summary>A constructor body (T1.1): pushes the class DefinitionScope and the instance's MemoryScope and ClassInstanceContext entry. Its return value is discarded.</summary>
        Constructor
    }

    /// <summary>
    /// A single activation record on the VM call stack.
    /// </summary>
    public struct CallFrame
    {
        public CompiledFunction Function;
        public int IP;
        public int StackBase;

        /// <summary>
        /// What the frame pushed on the context stacks when it was entered,
        /// and so what returning from it or unwinding it pops.
        /// </summary>
        public FrameKind Kind;

        /// <summary>
        /// For a <see cref="FrameKind.Constructor"/> frame: the instance (or
        /// base-class part of one) whose constructor body it runs.
        /// </summary>
        public WarScript.Expression.Value.ClassData Instance;
    }
}
