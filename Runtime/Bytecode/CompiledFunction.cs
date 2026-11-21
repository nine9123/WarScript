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
        /// The body's locals (T3.4): the slots right after the parameters
        /// that the VM fills with null whenever it pushes a frame of this
        /// function (T3.5), so the code starts at height
        /// <see cref="Arity"/> + <see cref="ReservedLocals"/>. Always 0 for a
        /// script body and a constructor (the loader requires it). Set by the
        /// compiler; stored in the file since format version 8.
        /// </summary>
        public int ReservedLocals;

        /// <summary>
        /// The largest stack height the function reaches, relative to its
        /// frame's stack base, arguments and reserved locals included. Computed by
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
    /// The kind of a <see cref="CallFrame"/>: where its bare names that are not
    /// locals resolve, and whether its value is kept. Since T3.5 no frame
    /// pushes anything on the context stacks: a frame carries its
    /// <see cref="CallFrame.Instance"/> and <see cref="CallFrame.Definitions"/>.
    /// </summary>
    public enum FrameKind : byte
    {
        /// <summary>An entry frame (script body, host call, coroutine): names resolve in the globals.</summary>
        Script,
        /// <summary>A function or lambda call: names resolve in the globals; it takes its caller's instance and definitions.</summary>
        Function,
        /// <summary>A method call: names resolve in the globals; its <see cref="CallFrame.Instance"/> is the instance part whose class defines the method, its <see cref="CallFrame.Definitions"/> that class's definition scope.</summary>
        Method,
        /// <summary>A constructor body (T1.1): names resolve in the instance's fields, then the globals; its <see cref="CallFrame.Instance"/> is the instance part whose body it runs, its <see cref="CallFrame.Definitions"/> that class's definition scope. Its return value is discarded.</summary>
        Constructor,
        /// <summary>An import's top-level code (T3.7), run by <c>WarVM.Run</c> before the script body, as the bottom frame: names resolve in the globals; it has no instance, its <see cref="CallFrame.Definitions"/> are the import's. Its return value is discarded.</summary>
        Import
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
        /// What kind of code the frame runs (see <see cref="FrameKind"/>).
        /// </summary>
        public FrameKind Kind;

        /// <summary>
        /// What <c>this</c> is in the frame (T3.5): for a
        /// <see cref="FrameKind.Method"/> or <see cref="FrameKind.Constructor"/>
        /// frame the instance part whose method or constructor body it runs;
        /// a <see cref="FrameKind.Function"/> frame (a function, lambda or a
        /// method called by its bare name) takes its caller's, so a method
        /// called by bare name from another method sees the same instance; an
        /// entry frame (script body, host call, coroutine) has none. Null
        /// outside a class: <c>this</c> raises NameError there.
        /// </summary>
        public WarScript.Expression.Value.ClassData Instance;

        /// <summary>
        /// Where the frame looks up the functions it calls by name and the
        /// classes it instantiates (T3.5): a method or constructor frame's
        /// class definition scope, a function or lambda frame's caller's
        /// (as when the class scope was pushed on the DefinitionContext for
        /// the method and every call it made), an entry frame's the top of
        /// the DefinitionContext when it started (the instance's
        /// definitions), an import frame the import's (T3.7). Nothing is pushed on the DefinitionContext for a
        /// frame, so a suspended coroutine's frames keep theirs between ticks.
        /// </summary>
        public WarScript.Context.Definition.DefinitionScope Definitions;
    }
}
