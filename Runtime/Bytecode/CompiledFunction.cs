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

        public CompiledFunction(string name, int arity)
        {
            Name = name;
            Arity = arity;
            Chunk = new Chunk();
        }
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
        /// True if this frame is a class method call.
        /// On return, the VM pops the class context (DefinitionScope, MemoryScope, ClassInstanceContext).
        /// </summary>
        public bool IsMethodCall;

        /// <summary>
        /// True if the VM pushed a MemoryScope for this frame.
        /// On return, the VM pops it.
        /// </summary>
        public bool HasScope;

        /// <summary>
        /// VM scope depth at the time this frame was pushed.
        /// Used during exception unwinding to restore scope state.
        /// </summary>
        public int SavedScopeDepth;
    }
}
