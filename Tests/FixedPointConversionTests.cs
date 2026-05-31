using FixMath;
using NUnit.Framework;
using WarScript.Exception;
using WarScript.Expression.Value;
using WarScript.Parser;

namespace Tests
{
    /// <summary>
    /// C#-level edge cases for the fixed-point migration that are most precisely
    /// expressed against the API directly rather than through a script:
    ///   - WarValue.ToInt truncation toward zero (the (int)double replacement, D1)
    ///   - the distinction between ToInt / RoundToInt / FloorToInt / CeilToInt
    ///   - NumericLiteral.Parse exactness, truncation, range &amp; malformed handling
    ///   - WarValue numeric round-trip and ToString (FormatFractional) display
    /// </summary>
    [TestFixture]
    public class FixedPointConversionTests
    {
        // ──────────────────────────────────────────────────────────
        //  WarValue.ToInt — truncates toward zero (matches old (int)double)
        // ──────────────────────────────────────────────────────────

        [TestCase(2.0, 2)]
        [TestCase(2.9, 2)]
        [TestCase(2.1, 2)]
        [TestCase(0.99, 0)]
        [TestCase(-2.9, -2)]   // toward zero, NOT floor (-3)
        [TestCase(-2.1, -2)]
        [TestCase(-0.99, 0)]
        public void ToInt_TruncatesTowardZero(double input, int expected)
        {
            Assert.AreEqual(expected, WarValue.ToInt(F64.FromDouble(input)));
        }

        [Test]
        public void ToInt_DiffersFromFloorToInt_OnNegatives()
        {
            // The contract note in WarValue says FloorToInt must NOT be used for
            // index/count truncation because it differs on negatives. Pin that.
            var v = F64.FromDouble(-2.5);
            Assert.AreEqual(-2, WarValue.ToInt(v));        // truncate toward zero
            Assert.AreEqual(-3, F64.FloorToInt(v));        // floor toward -inf
        }

        [Test]
        public void RoundToInt_IsHalfUp()
        {
            Assert.AreEqual(1, F64.RoundToInt(F64.Half));            // 0.5 → 1
            Assert.AreEqual(3, F64.RoundToInt(F64.FromDouble(2.5))); // 2.5 → 3 (not banker's 2)
            Assert.AreEqual(1, F64.RoundToInt(F64.FromDouble(1.4)));
            Assert.AreEqual(2, F64.RoundToInt(F64.FromDouble(1.5)));
        }

        [Test]
        public void CeilToInt_And_FloorToInt()
        {
            Assert.AreEqual(3, F64.CeilToInt(F64.FromDouble(2.1)));
            Assert.AreEqual(2, F64.FloorToInt(F64.FromDouble(2.9)));
            Assert.AreEqual(-2, F64.CeilToInt(F64.FromDouble(-2.9)));
            Assert.AreEqual(-3, F64.FloorToInt(F64.FromDouble(-2.1)));
        }

        // ──────────────────────────────────────────────────────────
        //  NumericLiteral.Parse — exactness & determinism
        // ──────────────────────────────────────────────────────────

        [Test]
        public void Parse_Integer_Exact()
        {
            Assert.AreEqual(F64.FromInt(42), NumericLiteral.Parse("42"));
            Assert.AreEqual(F64.Zero, NumericLiteral.Parse("0"));
        }

        [Test]
        public void Parse_NegativeInteger_Exact()
        {
            Assert.AreEqual(F64.FromInt(-7), NumericLiteral.Parse("-7"));
        }

        [Test]
        public void Parse_Half_Exact()
        {
            Assert.AreEqual(F64.Half, NumericLiteral.Parse("0.5"));
        }

        [Test]
        public void Parse_RepresentableFraction_Exact()
        {
            // 99.5 and 0.25 are exactly representable.
            Assert.AreEqual(F64.FromInt(99) + F64.Half, NumericLiteral.Parse("99.5"));
            Assert.AreEqual(F64.FromRaw(1L << 30), NumericLiteral.Parse("0.25"));
        }

        [Test]
        public void Parse_PointSeven_DeterministicTruncation()
        {
            // (7 << 32) / 10 — exact integer division, identical on every platform.
            Assert.AreEqual(F64.FromRaw((7L << 32) / 10), NumericLiteral.Parse("0.7"));
        }

        [Test]
        public void Parse_TruncatesPastNineFractionalDigits()
        {
            // Only the first 9 fractional digits participate; the rest are dropped.
            Assert.AreEqual(NumericLiteral.Parse("0.123456789"),
                            NumericLiteral.Parse("0.123456789999"));
        }

        [Test]
        public void Parse_TrailingZeros_DoNotChangeValue()
        {
            Assert.AreEqual(NumericLiteral.Parse("1.5"), NumericLiteral.Parse("1.50000"));
        }

        [Test]
        public void Parse_MaxIntegerPart_Succeeds()
        {
            Assert.AreEqual(F64.FromInt(2147483647), NumericLiteral.Parse("2147483647"));
        }

        // ── Malformed / out-of-range → SyntaxException (D2a, never silent) ──

        [Test]
        public void Parse_OutOfRangeInteger_Throws()
        {
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("2147483648"));
        }

        [Test]
        public void Parse_WayOutOfRange_Throws()
        {
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("99999999999"));
        }

        [Test]
        public void Parse_EmptyString_Throws()
        {
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse(""));
        }

        [Test]
        public void Parse_BareNegativeSign_Throws()
        {
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("-"));
        }

        [Test]
        public void Parse_DoubleDot_Throws()
        {
            // A second '.' is a genuine malformed literal the parser must reject.
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("1.2.3"));
        }

        [Test]
        public void Parse_NonDigitCharacter_Throws()
        {
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("1.2.3"));
            Assert.Throws<SyntaxException>(() => NumericLiteral.Parse("12a"));
        }

        // ──────────────────────────────────────────────────────────
        //  WarValue numeric round-trip & ToString (FormatFractional)
        // ──────────────────────────────────────────────────────────

        [Test]
        public void FromNumeric_Numeric_RoundTrips()
        {
            var v = WarValue.FromNumeric(F64.FromInt(123) + F64.Half);
            Assert.AreEqual(F64.FromInt(123) + F64.Half, v.Numeric);
            Assert.IsTrue(v.IsNumeric);
        }

        [Test]
        public void FromNumeric_Int_Overload_RoundTrips()
        {
            var v = WarValue.FromNumeric(255);
            Assert.AreEqual(F64.FromInt(255), v.Numeric);
            Assert.AreEqual(255, WarValue.ToInt(v.Numeric));
        }

        [Test]
        public void ToString_IntegerValued_NoDecimalPoint()
        {
            // An integer-valued numeric prints without a fractional tail.
            Assert.AreEqual("42", WarValue.FromNumeric(F64.FromInt(42)).ToString());
        }

        [Test]
        public void ToString_Fractional_ShortestRoundTrip()
        {
            // FormatFractional prints the shortest decimal that round-trips to the
            // same raw — 0.5 → "0.5", 99.5 → "99.5" (not a long binary expansion).
            Assert.AreEqual("0.5", WarValue.FromNumeric(F64.Half).ToString());
            Assert.AreEqual("99.5", WarValue.FromNumeric(F64.FromInt(99) + F64.Half).ToString());
        }

        [Test]
        public void ToString_NegativeFractional()
        {
            Assert.AreEqual("-2.5", WarValue.FromNumeric(-(F64.Two + F64.Half)).ToString());
        }

        [Test]
        public void Logical_BackedByNumeric_TrueFalse()
        {
            // true/false are numeric-backed (1/0); confirm the bridge holds.
            Assert.IsTrue(WarValue.True.LogicalValue);
            Assert.IsFalse(WarValue.False.LogicalValue);
            Assert.AreEqual(F64.One, WarValue.True.Numeric);
            Assert.AreEqual(F64.Zero, WarValue.False.Numeric);
        }
    }
}
