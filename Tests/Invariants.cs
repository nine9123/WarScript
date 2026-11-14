using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using WarScript;

namespace Tests
{
    /// <summary>
    /// Runtime invariants checked after every script entry made through the
    /// test helpers and the golden runner (v2 plan, section 9.3). A violation
    /// fails the test with the name of the invariant. The checks themselves
    /// are in Invariants.Checks.cs.
    /// </summary>
    public static partial class Invariants
    {
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
