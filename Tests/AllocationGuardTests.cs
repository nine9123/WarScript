using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Allocation guards (plan T1.4): text and array operations check the
    /// resulting size against <see cref="WarScriptLanguage.MaxTextLength"/> /
    /// <see cref="WarScriptLanguage.MaxArrayLength"/>, and the memory budget,
    /// before they allocate. Each guard is tested with a size exactly at the
    /// limit (succeeds) and one past it (a rescuable error).
    /// </summary>
    [TestFixture]
    public class AllocationGuardTests
    {
        private static List<string> RunWithLimits(string source, int maxText = 1_048_576, int maxArray = 1_048_576,
            long memoryBudget = 0)
        {
            var (_, output) = TestHelper.Run("t", source, (s, scope) =>
            {
                s.MaxTextLength = maxText;
                s.MaxArrayLength = maxArray;
                s.MemoryBudget = memoryBudget;
                ArrayLibrary.Register(s, scope);
            });
            return output;
        }

        /// <summary>Runs <paramref name="expression"/> as an assignment and prints its length, or the rescued error.</summary>
        private static string Outcome(string setup, string expression, int maxText = 1_048_576, int maxArray = 1_048_576)
        {
            var output = RunWithLimits(setup + @"
begin
    r = " + expression + @"
    print Array_length [r]
rescue e
    print e
end", maxText, maxArray);
            Assert.AreEqual(1, output.Count, string.Join(" | ", output));
            return output[0];
        }

        [Test]
        public void DefaultLimits_ArePROP5()
        {
            var script = new WarScriptLanguage("t", "", null, null);
            Assert.AreEqual(1_048_576, script.MaxTextLength);
            Assert.AreEqual(1_048_576, script.MaxArrayLength);
        }

        // ── Text ────────────────────────────────────────────────────────

        /// <summary>Runs <paramref name="expression"/> as an assignment and prints it, or the rescued error.</summary>
        private static string TextOutcome(string setup, string expression, int maxText)
        {
            var output = RunWithLimits(setup + @"
begin
    r = " + expression + @"
    print r
rescue e
    print e
end", maxText);
            Assert.AreEqual(1, output.Count, string.Join(" | ", output));
            return output[0];
        }

        [Test]
        public void Concatenation_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("1234512345", TextOutcome(@"a = ""12345""", "a + a", 10));
            Assert.AreEqual("Text too long", TextOutcome(@"a = ""12345""", @"a + a + ""x""", 10));
            Assert.AreEqual("Text too long", TextOutcome(@"a = ""1234567890""", "a + 1", 10));
        }

        [Test]
        public void Repetition_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("ababababab", TextOutcome("", @"""ab"" * 5", 10));
            Assert.AreEqual("ababababab", TextOutcome("", @"5 * ""ab""", 10));
            Assert.AreEqual("Text too long", TextOutcome("", @"""ab"" * 6", 10));
            Assert.AreEqual("Text too long", TextOutcome("", @"6 * ""ab""", 10));
        }

        [Test]
        public void Repetition_ByZeroOrLess_IsEmpty()
        {
            Assert.AreEqual("", TextOutcome("", @"""ab"" * 0", 10));
            Assert.AreEqual("", TextOutcome("", @"""ab"" * (0 - 3)", 10));
        }

        [Test]
        public void Repetition_WhoseLengthOverflowsInt_IsTextTooLong()
        {
            // 2 * 2,000,000,000 overflows int; the guard computes in long.
            Assert.AreEqual("Text too long", TextOutcome("", @"""ab"" * 2000000000", int.MaxValue));
        }

        [Test]
        public void IndexWrite_AtTheLimitSucceeds_PastItFails()
        {
            // s{0} = "ab" replaces one character with two.
            var output = RunWithLimits(@"
s = ""123456789""
s{0} = ""ab""
print s
t = ""1234567890""
begin
    t{0} = ""ab""
rescue e
    print e
end
print t
t{10} = ""ab""
print t", maxText: 10);
            // Out of range (the last write): nothing changes, so nothing is checked.
            CollectionAssert.AreEqual(new[] { "ab23456789", "Text too long", "1234567890", "1234567890" }, output);
        }

        [Test]
        public void SubtractingEmptyText_LeavesTheTextAsItIs()
        {
            var output = RunWithLimits(@"print ""abc"" - """"");
            CollectionAssert.AreEqual(new[] { "abc" }, output);
        }

        // ── Arrays ──────────────────────────────────────────────────────

        [Test]
        public void ArrayConcatenation_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("3", Outcome("", "{1, 2} + {3}", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("", "{1, 2} + {3, 4}", maxArray: 3));
            Assert.AreEqual("3", Outcome("", "{1, 2} + 3", maxArray: 3));
            Assert.AreEqual("3", Outcome("", "1 + {2, 3}", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("", "{1, 2, 3} + 4", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("", "0 + {1, 2, 3}", maxArray: 3));
        }

        [Test]
        public void Append_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("3", Outcome("a = {1, 2}", "a << 3", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("a = {1, 2, 3}", "a << 4", maxArray: 3));
        }

        [Test]
        public void Literal_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("3", Outcome("", "{1, 2, 3}", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("", "{1, 2, 3, 4}", maxArray: 3));
        }

        [Test]
        public void ArrayInsert_AtTheLimitSucceeds_PastItFails()
        {
            Assert.AreEqual("3", Outcome("a = {1, 2}", "Array_insert [a, 0, 9]", maxArray: 3));
            Assert.AreEqual("Array too long", Outcome("a = {1, 2, 3}", "Array_insert [a, 0, 9]", maxArray: 3));
        }

        [Test]
        public void FailedAppend_LeavesTheArrayUnchanged()
        {
            var output = RunWithLimits(@"
a = {1, 2, 3}
begin
    a << 4
rescue e
    print e
end
print Array_length [a]", maxArray: 3);
            CollectionAssert.AreEqual(new[] { "Array too long", "3" }, output);
        }

        // ── Memory budget ───────────────────────────────────────────────

        [Test]
        public void MemoryBudget_IsCheckedBeforeTheAllocation()
        {
            // With the text limit out of the way, 2,000,000,000 characters
            // cannot be allocated at all (.NET strings stop near 2^30), so
            // this only succeeds if the budget is checked first.
            var output = RunWithLimits(@"
begin
    s = ""a"" * 2000000000
rescue e
    print e
end", maxText: int.MaxValue, memoryBudget: 1000);
            CollectionAssert.AreEqual(new[] { "Memory budget exceeded" }, output);
        }

        [Test]
        public void MemoryBudget_FailedAppendDoesNotGrowTheArray()
        {
            // '{}' costs 64 estimated bytes and each append 16: a budget of
            // 64 + 10 * 16 allows exactly 10 appends; the 11th fails before
            // it changes the array.
            var output = RunWithLimits(@"
a = {}
begin
    loop true
        a << 1
    end
rescue e
    print e
end
print Array_length [a]", memoryBudget: 64 + 10 * 16);
            CollectionAssert.AreEqual(new[] { "Memory budget exceeded", "10" }, output);
        }

        [Test]
        public void MemoryBudget_FailedBaseClassAllocationRunsNoConstructor()
        {
            // '{}' costs 64 estimated bytes and an instance with one property
            // 16 + 96: the derived part fits the budget, its base part does
            // not. No constructor body may run once the budget error is raised.
            var output = RunWithLimits(@"
log = {}
class B [x]
    log << ""B ctor""
end
class D [x] : B [x]
    log << ""D ctor""
end
begin
    d = new D [1]
    print ""created""
rescue e
    print e
end
print Array_length [log]", memoryBudget: 2 * 112 - 1 + 64);
            CollectionAssert.AreEqual(new[] { "Memory budget exceeded", "0" }, output);
        }
    }
}
