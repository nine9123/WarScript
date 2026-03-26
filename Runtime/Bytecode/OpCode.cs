namespace WarScript.Bytecode
{
    public enum OpCode : byte
    {
        // ── Constants & Literals ──
        Constant,       // [op][hi][lo]  → push Constants[hi<<8|lo]
        Null,           // push null
        True,           // push true
        False,          // push false

        // ── Stack management ──
        Pop,            // discard TOS
        PopN,           // [op][n]  → discard n values
        Dup,            // duplicate TOS

        // ── Scope management ──
        PushScope,      // push a new MemoryScope (for if-blocks, etc.)
        PopScope,       // pop the current MemoryScope

        // ── Variables ──
        GetLocal,       // [op][hi][lo]  → push stack[base+slot]
        SetLocal,       // [op][hi][lo]  → stack[base+slot] = TOS (no pop)
        GetGlobal,      // [op][hi][lo]  → push globals[Constants[idx].TextValue]
        SetGlobal,      // [op][hi][lo]  → globals[Constants[idx].TextValue] = TOS (no pop)

        // ── Arithmetic ──
        Add,
        Sub,
        Mul,
        Div,
        Mod,
        Negate,

        // ── Comparison ──
        Equal,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,

        // ── Logical ──
        Not,

        // ── Control flow ──
        Jump,           // [op][hi][lo]  → unconditional forward jump
        JumpIfFalse,    // [op][hi][lo]  → jump if TOS is falsy (does NOT pop)
        JumpIfTrue,     // [op][hi][lo]  → jump if TOS is truthy (does NOT pop)
        Loop,           // [op][hi][lo]  → unconditional backward jump

        // ── Functions ──
        Call,           // [op][name_hi][name_lo][arg_count]
        TailCall,       // [op][name_hi][name_lo][arg_count] — reuses current frame
        Return,         // return TOS

        // ── Arrays ──
        NewArray,       // [op][count_hi][count_lo] → pop count values, push array
        IndexGet,       // pop index, pop target, push element
        IndexSet,       // pop value, pop index, pop target → set, push value
        IndexSetLocal,  // [op][slot_hi][slot_lo] → pop value, pop index, read local → set element, writeback for text, push value
        IndexSetGlobal, // [op][name_hi][name_lo] → pop value, pop index, read global → set element, writeback for text, push value
        IndexSetProp,   // [op][name_hi][name_lo] → pop value, pop index, pop instance → set element on property, writeback for text, push value
        ArrayAppend,    // pop value, peek array → append, leave array

        // ── Iteration helpers ──
        Len,            // pop target → push length (array count, text length, class prop count)
        IterPrepare,    // pop target → if array leave it; if class convert to property values array

        // ── Classes ──
        NewInstance,    // [op][name_hi][name_lo][arg_count]
        NewNestedInstance, // [op][name_hi][name_lo][arg_count] — parent instance under args
        GetProperty,    // [op][name_hi][name_lo] → pop instance, push property value
        SetProperty,    // [op][name_hi][name_lo] → pop value, pop instance, set prop, push value
        CallMethod,     // [op][name_hi][name_lo][arg_count]
        This,           // push current class instance
        CastAs,         // [op][name_hi][name_lo] → pop instance, push cast result or null
        InstanceOf,     // [op][name_hi][name_lo] → pop instance, push bool

        // ── Builtins ──
        Print,          // pop TOS, print
        Assert,         // pop TOS, assert truthy

        // ── Exception handling ──
        PushHandler,    // [op][rescue_hi][rescue_lo][ensure_hi][ensure_lo][end_hi][end_lo]
        PopHandler,     // pop the innermost handler (normal exit)
        Raise,          // pop TOS, raise as exception message

        // ── Coroutines ──
        Yield,          // yield NextTick
        YieldWait,      // pop duration, yield Wait
        YieldUntil,     // (compiled as separate condition — reserved)

        // ── Import ──
        Import,         // [op][path_hi][path_lo]
    }
}
