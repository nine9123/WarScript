#nullable enable

namespace WarScript.Bytecode
{
    /// <summary>
    /// The instruction and memory budgets of one <see cref="WarScriptLanguage"/>
    /// instance (plan T1.5, PROP-2). Every VM of the instance charges the
    /// same counters: the cached VM, the VM a nested host entry runs on,
    /// coroutine VMs and import VMs. Only an outermost entry (<c>Run</c>,
    /// <c>Call</c>, a coroutine start or resume made by the host) resets them,
    /// so neither a constructor, an import nor a native that calls back into
    /// the instance gets a fresh budget (BUDGET-1).
    /// </summary>
    internal sealed class ExecutionBudget
    {
        /// <summary>
        /// Instructions that may still be dispatched before the next one
        /// raises "Instruction budget exceeded": the dispatch loop decrements
        /// it and raises when it drops below zero. <see cref="long.MaxValue"/>
        /// when the budget is unlimited (too many instructions to ever run out).
        /// </summary>
        public long InstructionsLeft = long.MaxValue;

        /// <summary>Estimated bytes allocated since the outermost entry started.</summary>
        public long MemoryUsed;

        /// <summary>Most bytes the entry may allocate; 0 means unlimited.</summary>
        public long MemoryLimit;

        /// <summary>
        /// The "Instruction budget exceeded" error, once raised: every further
        /// instruction of the entry raises this same error again (BUDGET-2),
        /// so it keeps the line and call stack of the instruction that ran
        /// out (plan T2.5), not those of the handler it was re-raised in.
        /// </summary>
        public WarScript.Exception.Exception? Exhausted;

        /// <summary>
        /// Starts a new outermost entry. A budget of <c>n</c> instructions
        /// raises on the <c>n</c>-th dispatch, as v1 did; 0 or less is unlimited.
        /// </summary>
        public void Reset(int instructionBudget, long memoryBudget)
        {
            InstructionsLeft = instructionBudget > 0 ? instructionBudget - 1L : long.MaxValue;
            MemoryUsed = 0;
            MemoryLimit = memoryBudget;
            Exhausted = null;
        }
    }
}
