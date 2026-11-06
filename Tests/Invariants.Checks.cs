using WarScript;

// No NUnit in this file: Headless~/Runner/WarScript.Runner.csproj compiles it
// too, to check the invariants inside the child-process runner (plan 9.5).
namespace Tests
{
    public static partial class Invariants
    {
        /// <summary>Context depths recorded before an entry.</summary>
        public readonly struct Snapshot
        {
            public readonly int MemoryDepth;
            public readonly int DefinitionDepth;

            public Snapshot(int memoryDepth, int definitionDepth)
            {
                MemoryDepth = memoryDepth;
                DefinitionDepth = definitionDepth;
            }
        }

        public static Snapshot Capture(WarScriptLanguage script)
        {
            return new Snapshot(script.MemoryContext.Depth, script.DefinitionContext.Depth);
        }

        /// <summary>
        /// Returns the first violated invariant as "<name>: <detail>", or null.
        /// <paramref name="outermost"/> enables the checks that only hold once
        /// control is back with the host.
        /// </summary>
        public static string Violation(WarScriptLanguage script, Snapshot before, bool outermost = true)
        {
            if (script.MemoryContext.Depth != before.MemoryDepth)
                return $"MemoryContextDepth: {before.MemoryDepth} before the entry, {script.MemoryContext.Depth} after";

            if (script.DefinitionContext.Depth != before.DefinitionDepth)
                return $"DefinitionContextDepth: {before.DefinitionDepth} before the entry, {script.DefinitionContext.Depth} after";

            if (script.ExceptionContext.IsRaised())
                return "ExceptionState: an exception is still raised after the entry (v1 logs and clears unhandled errors)";

            if (!outermost)
                return null;

            if (script.ClassInstanceContext.Depth != 0)
                return $"ClassInstanceContextEmpty: {script.ClassInstanceContext.Depth} instance(s) left pushed";

            if (!script.IsYielded && !script.IsVMIdle)
                return "VMIdle: frames, stack values, handlers or a pending return/exception left behind";

            return null;
        }
    }
}
