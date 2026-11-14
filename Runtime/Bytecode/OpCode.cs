namespace WarScript.Bytecode
{
    /// <summary>
    /// The VM's instructions. Every member has an explicit value (v2 plan
    /// T1.7, DEC-18): never renumber one and never reuse a removed value
    /// (7 and 8 were PushScope and PopScope, 58 YieldUntil, 61 to 65 the
    /// unused fused compare-and-jumps).
    /// Operands, stack effects and the format hash come from
    /// <see cref="OpInfo"/>; a change here needs a new
    /// <see cref="BytecodeFormat.Version"/>.
    /// </summary>
    public enum OpCode : byte
    {
        // ── Constants & Literals ──
        Constant = 0,   // [op][hi][lo]  → push Constants[hi<<8|lo]
        Null = 1,       // push null
        True = 2,       // push true
        False = 3,      // push false

        // ── Stack management ──
        Pop = 4,        // discard TOS
        PopN = 5,       // [op][n]  → discard n values
        Dup = 6,        // duplicate TOS

        // ── Variables ──
        GetLocal = 9,   // [op][hi][lo]  → push stack[base+slot]
        SetLocal = 10,  // [op][hi][lo]  → stack[base+slot] = TOS (no pop)
        GetGlobal = 11, // [op][hi][lo]  → push globals[Constants[idx].TextValue]
        SetGlobal = 12, // [op][hi][lo]  → globals[Constants[idx].TextValue] = TOS (no pop)

        // ── Arithmetic ──
        Add = 13,
        Sub = 14,
        Mul = 15,
        Div = 16,
        Mod = 17,
        Negate = 18,

        // ── Comparison ──
        Equal = 19,
        NotEqual = 20,
        Less = 21,
        LessEqual = 22,
        Greater = 23,
        GreaterEqual = 24,

        // ── Logical ──
        Not = 25,

        // ── Control flow ──
        Jump = 26,      // [op][hi][lo]  → unconditional forward jump
        JumpIfFalse = 27, // [op][hi][lo]  → jump if TOS is falsy (does NOT pop)
        JumpIfTrue = 28, // [op][hi][lo]  → jump if TOS is truthy (does NOT pop)
        Loop = 29,      // [op][hi][lo]  → unconditional backward jump

        // ── Functions ──
        Call = 30,      // [op][name_hi][name_lo][arg_count]
        TailCall = 31,  // [op][name_hi][name_lo][arg_count] — reuses current frame
        CallValue = 32, // [op][arg_count] → pop func value from under args, call it
        Return = 33,    // return TOS

        // ── Arrays ──
        NewArray = 34,  // [op][count_hi][count_lo] → pop count values, push array
        IndexGet = 35,  // pop index, pop target, push element
        IndexSet = 36,  // pop value, pop index, pop target → set, push value
        IndexSetLocal = 37, // [op][slot_hi][slot_lo] → pop value, pop index, read local → set element, writeback for text, push value
        IndexSetGlobal = 38, // [op][name_hi][name_lo] → pop value, pop index, read global → set element, writeback for text, push value
        IndexSetProp = 39, // [op][name_hi][name_lo][cache_hi][cache_lo] → pop value, pop index, pop instance → set element on property, writeback for text, push value
        ArrayAppend = 40, // pop value, peek array → append, leave array

        // ── Iteration helpers ──
        Len = 41,       // pop target → push length (array count, text length, class prop count)
        IterPrepare = 42, // pop target → if array leave it; if class convert to property values array

        // ── Classes ──
        NewInstance = 43, // [op][name_hi][name_lo][arg_count]
        NewNestedInstance = 44, // [op][name_hi][name_lo][arg_count] — parent instance under args
        GetProperty = 45, // [op][name_hi][name_lo][cache_hi][cache_lo] → pop instance, push property value
        SetProperty = 46, // [op][name_hi][name_lo][cache_hi][cache_lo] → pop value, pop instance, set prop, push value
        CallMethod = 47, // [op][name_hi][name_lo][arg_count] — instance under args
        This = 48,      // push current class instance
        CastAs = 49,    // [op][name_hi][name_lo] → pop instance, push cast result or null
        InstanceOf = 50, // [op][name_hi][name_lo] → pop instance, push bool

        // ── Builtins ──
        Print = 51,     // pop TOS, print
        Assert = 52,    // pop TOS, assert truthy

        // ── Exception handling ──
        PushHandler = 53, // [op][rescue_hi][rescue_lo][ensure_hi][ensure_lo][end_hi][end_lo][has_rescue]
        PopHandler = 54, // pop the innermost handler (normal exit)
        Raise = 55,     // pop TOS, raise as exception message

        // ── Coroutines ──
        Yield = 56,     // yield NextTick
        YieldWait = 57, // pop duration, yield Wait

        // ── Import ──
        Import = 59,    // [op][path_hi][path_lo]

        // ── Superinstructions ──
        // Fused opcode pairs to reduce dispatch overhead.
        // Each replaces 2–3 instructions with a single dispatch.

        // Less + JumpIfFalse + Pop: pop two operands, compare,
        // jump if not less. No intermediate boolean on stack.
        LessJump = 60,      // [op][offset_hi][offset_lo]

        // This + GetProperty: push this, then read property.
        ThisGetProperty = 66, // [op][name_hi][name_lo][cache_hi][cache_lo]

        // This + SetProperty: set property on this.
        ThisSetProperty = 67, // [op][name_hi][name_lo][cache_hi][cache_lo]

        // ── Leaving an ensure block by break/next ──
        // Discard the top pending re-raise/return if it belongs to this frame's
        // ensure block ending at end_ip (Q-12, CRASH-8).
        DropPending = 68,   // [op][end_hi][end_lo]
    }
}
