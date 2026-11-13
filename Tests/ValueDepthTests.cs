using System.Collections.Generic;
using NUnit.Framework;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Cycle-safe, depth-limited value operations (plan T1.2, CRASH-1) as the
    /// host sees them. The script side (formatting, '==', the array natives)
    /// is in Golden~/language/value_depth.ws and Golden~/repro/A01, A02.
    /// </summary>
    [TestFixture]
    public class ValueDepthTests
    {
        private static WarValue Cyclic(int first)
        {
            var list = new List<WarValue> { WarValue.FromNumeric(first) };
            var value = WarValue.FromArray(list);
            list.Add(value);
            return value;
        }

        private static WarValue Nested(int levels)
        {
            var value = WarValue.FromArray(new List<WarValue>());
            for (int i = 0; i < levels; i++)
                value = WarValue.FromArray(new List<WarValue> { value });
            return value;
        }

        [Test]
        public void CyclicArray_FormatsWithAMarker()
        {
            Assert.AreEqual("[1, [...]]", Cyclic(1).ToString());
        }

        [Test]
        public void CyclicArrays_HashInBoundedTime_AndEqualOnesHashEqual()
        {
            Assert.AreEqual(Cyclic(1).GetHashCode(), Cyclic(1).GetHashCode());
            Assert.AreEqual(Nested(10_000).GetHashCode(), Nested(10_000).GetHashCode());
        }

        [Test]
        public void EqualityBeyondMaxValueDepth_ThrowsTheLimitException()
        {
            // The innermost array of Nested(n) is n levels below the outermost.
            Assert.IsTrue(Nested(WarValue.MaxValueDepth - 1).Equals(Nested(WarValue.MaxValueDepth - 1)));
            var e = Assert.Throws<WarScriptLimitException>(
                () => Nested(WarValue.MaxValueDepth).Equals(Nested(WarValue.MaxValueDepth)));
            Assert.AreEqual(ScriptErrorKind.LimitExceeded, e.Kind);
            Assert.AreEqual("Value nested too deeply", e.Message);
            Assert.Throws<WarScriptLimitException>(() => Cyclic(1).Equals(Cyclic(1)));
        }

        [Test]
        public void ArraysThatDifferBeforeTheLimit_CompareWithoutError()
        {
            // Counts differ at the top: no element is compared.
            Assert.IsFalse(Cyclic(1).Equals(Nested(10_000)));
            // The first elements differ before the cycle is followed.
            Assert.IsFalse(Cyclic(1).Equals(Cyclic(2)));
        }
    }
}
