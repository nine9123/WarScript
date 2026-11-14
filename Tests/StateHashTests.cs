using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// <see cref="WarScriptLanguage.ComputeStateHash"/> (v2 plan T0.6, section 9.7).
    /// The golden runner additionally checks that every golden ends with the
    /// same hash in both modes and every culture.
    /// </summary>
    [TestFixture]
    public class StateHashTests
    {
        private static ulong HashOf(string source)
        {
            var (script, _) = TestHelper.Run("hash", source);
            return script.ComputeStateHash();
        }

        [Test]
        public void EncodingIsPinned()
        {
            // FNV-1a 64 over: "a" (UTF-16LE), 0x0A 0x00, 0xFF, "x" (UTF-16LE),
            // 0x00 0x00, tag Numeric (1), raw 1.0 = 0x0000000100000000 (LE).
            // Computed independently from the plan's byte layout; a change here
            // changes every recorded baseline (T0.9).
            Assert.AreEqual(0x6603f360dd42de3fUL, HashOf("x = 1\nprint \"a\""));

            // 0xFF, "t", 0x00 0x00, tag Text (3), length 2 (4 bytes LE), "hi".
            Assert.AreEqual(0x891a18d56a15e040UL, HashOf("t = \"hi\""));
        }

        [Test]
        public void StateWithoutGlobalsHashesOnlyThePrintedLines()
        {
            // An instance that printed nothing and holds no user globals hashes
            // to FNV-1a 64 over the single byte 0xFF. BASELINE.md quotes these
            // values: a recorded hash equal to one of them carries no script
            // state, only the printed lines.
            Assert.AreEqual(0xaf64724c8602eb6eUL, HashOf("fun f []\n    return 1\nend"));
            Assert.AreEqual(0xdc553fa85c69d526UL, HashOf("print \"bootstrap\""));
        }

        [Test]
        public void IdenticalRunsHashIdentically()
        {
            const string source = @"
class P [x, y]
    sum = x + y
end
p = new P [1, 2]
arr = {1, ""two"", {3.5, true}, null, p}
print ""done""";
            Assert.AreEqual(HashOf(source), HashOf(source));
        }

        [Test]
        public void PrintedLinesAndGlobalsBothCount()
        {
            var baseline = HashOf("x = 1\nprint \"a\"");
            Assert.AreNotEqual(baseline, HashOf("x = 1\nprint \"b\""));
            Assert.AreNotEqual(baseline, HashOf("x = 2\nprint \"a\""));
            Assert.AreNotEqual(baseline, HashOf("y = 1\nprint \"a\""));
            // A line boundary is part of the encoding.
            Assert.AreNotEqual(HashOf("print \"ab\""), HashOf("print \"a\"\nprint \"b\""));
        }

        [Test]
        public void GlobalsAreHashedInNameOrder()
        {
            Assert.AreEqual(HashOf("a = 1\nb = 2"), HashOf("b = 2\na = 1"));
        }

        [Test]
        public void ValuesOfDifferentTypesDiffer()
        {
            var hashes = new HashSet<ulong>
            {
                HashOf("v = 1"), HashOf("v = \"1\""), HashOf("v = true"), HashOf("v = null"), HashOf("v = {1}")
            };
            Assert.AreEqual(5, hashes.Count);
        }

        [Test]
        public void InstanceFieldsCount()
        {
            const string cls = "class P [x]\n    y = x * 2\nend\n";
            Assert.AreNotEqual(HashOf(cls + "p = new P [1]"), HashOf(cls + "p = new P [2]"));
        }

        [Test]
        public void HostGlobalsAreExcluded()
        {
            var (plain, _) = TestHelper.Run("hash", "x = 1");
            var (withHost, _) = TestHelper.Run("hash", "x = 1");
            withHost.GlobalMemoryScope.Set("HOST_ONLY", WarValue.FromNumeric(5));
            Assert.AreEqual(plain.ComputeStateHash(), withHost.ComputeStateHash());
        }

        [Test]
        public void CyclicArrayIsHashedWithAMarker()
        {
            // Formatting or comparing this array overflows the stack in v1
            // (CRASH-1); hashing it must not.
            var cyclic = HashOf("a = {1}\na << a");
            var flat = HashOf("a = {1}\na << {1}");
            Assert.AreNotEqual(cyclic, flat);
            Assert.AreEqual(cyclic, HashOf("a = {1}\na << a"));
        }

        [Test]
        public void DeepNestingIsCutOff()
        {
            // Deeper than MaxValueDepth (256): the hash stays finite and stable.
            const string source = "a = {}\nloop i in 0..1000\n    a = {a}\nend";
            Assert.AreEqual(HashOf(source), HashOf(source));
        }
    }
}
