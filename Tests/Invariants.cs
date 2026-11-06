using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using WarScript;

namespace Tests
{
    /// <summary>
    /// Runtime invariants checked after every script entry made through the
    /// test helpers and the golden runner (v2 plan, section 9.3). A violation
    /// fails the test with the name of the invariant.
    /// </summary>
    public static class Invariants
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

        /// <summary>
        /// Fails the current test when an invariant does not hold — unless the
        /// test is marked <see cref="KnownInvariantViolationAttribute"/>, in
        /// which case the violation is logged and the test carries on so its
        /// own assertions still run.
        /// </summary>
        public static void Check(WarScriptLanguage script, Snapshot before, bool outermost = true)
        {
            var violation = Violation(script, before, outermost);
            if (violation == null)
                return;

            var known = TestContext.CurrentContext.Test.Properties.Get(KnownInvariantViolationAttribute.PropertyName) as string;
            if (known == null)
                Assert.Fail("Invariant violated: " + violation);

            _knownViolations++;
            TestContext.WriteLine($"Known invariant violation ({known}): {violation}");
        }

        [ThreadStatic] private static int _knownViolations;

        internal static void ResetKnownViolations() => _knownViolations = 0;
        internal static int KnownViolations => _knownViolations;
    }

    /// <summary>
    /// Marks a test whose script run is known to break an invariant until the
    /// named finding or task is fixed. The test still runs all its own
    /// assertions. If no invariant is violated during the test, it fails so
    /// the stale marker gets removed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class KnownInvariantViolationAttribute : PropertyAttribute, ITestAction
    {
        public const string PropertyName = "KnownInvariantViolation";

        public KnownInvariantViolationAttribute(string findingId) : base(PropertyName, findingId) { }

        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test) => Invariants.ResetKnownViolations();

        public void AfterTest(ITest test)
        {
            if (Invariants.KnownViolations == 0)
                Assert.Fail($"Test is marked [KnownInvariantViolation(\"{test.Properties.Get(PropertyName)}\")] " +
                            "but every invariant held: remove the marker.");
        }
    }
}
