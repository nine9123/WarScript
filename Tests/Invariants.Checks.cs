using WarScript;

// No NUnit in this file: Headless~/Runner/WarScript.Runner.csproj compiles it
// too, to check the invariants inside the child-process runner (plan 9.5).
namespace Tests
{
    public static partial class Invariants
    {
        /// <summary>Context depth recorded before an entry (the MemoryContext stack went with T3.5).</summary>
        public readonly struct Snapshot
        {
            public readonly int DefinitionDepth;

            public Snapshot(int definitionDepth)
            {
                DefinitionDepth = definitionDepth;
            }
        }

        public static Snapshot Capture(WarScriptLanguage script)
        {
            return new Snapshot(script.DefinitionContext.Depth);
        }

        /// <summary>
        /// Returns the first violated invariant as "<name>: <detail>", or null.
        /// <paramref name="outermost"/> enables the checks that only hold once
        /// control is back with the host.
        /// </summary>
        public static string Violation(WarScriptLanguage script, Snapshot before, bool outermost = true)
        {
            if (script.DefinitionContext.Depth != before.DefinitionDepth)
                return $"DefinitionContextDepth: {before.DefinitionDepth} before the entry, {script.DefinitionContext.Depth} after";

            if (script.ExceptionContext.IsRaised())
                return "ExceptionState: an exception is still raised after the entry (v1 logs and clears unhandled errors)";

            if (!outermost)
                return null;

            if (script.EntryDepth != 0)
                return $"EntryDepth: {script.EntryDepth} host entr(y/ies) still counted as running";

            if (!script.IsYielded && !script.IsVMIdle)
                return "VMIdle: frames, stack values, handlers or a pending return/exception left behind";

            return null;
        }
    }
}
