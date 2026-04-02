using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Token;

namespace Tests
{
    /// <summary>
    /// Tests for numeric literal separators.
    /// Underscores inside numeric literals are stripped by the lexer and
    /// have no effect on the value — they exist purely for readability.
    ///
    /// Rules:
    ///   - Allowed anywhere between digits in integer and decimal parts
    ///   - Multiple consecutive underscores allowed
    ///   - Stripped before double.Parse, so the token value is always clean
    ///   - Leading/trailing underscores on the whole literal are NOT digits
    ///     and are therefore not part of a numeric token (they'd start an identifier)
    /// </summary>
    [TestFixture]
    public class NumericSeparatorTests
    {
        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private static Token LexSingle(string src)
        {
            var tokens = LexicalParser.Parse(src);
            // Strip any LineBreak/Comment tokens
            var numeric = tokens.FindAll(t => t.Type == TokenType.Numeric);
            Assert.AreEqual(1, numeric.Count, $"Expected exactly one Numeric token in: {src}");
            return numeric[0];
        }

        private static List<string> Run(string source)
        {
            LexicalParser.ClearCache();
            var (_, output) = TestHelper.Run("test", source);
            return output;
        }

        // ─────────────────────────────────────────────────────────────
        // Lexer — token value is stripped of underscores
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Lexer_NoSeparator_Unchanged()
        {
            var t = LexSingle("1000000");
            Assert.AreEqual("1000000", t.Value);
        }

        [Test]
        public void Lexer_ThousandsSeparator_Stripped()
        {
            var t = LexSingle("1_000_000");
            Assert.AreEqual("1000000", t.Value);
        }

        [Test]
        public void Lexer_SingleSeparator_Stripped()
        {
            var t = LexSingle("1_0");
            Assert.AreEqual("10", t.Value);
        }

        [Test]
        public void Lexer_MultipleSeparators_AllStripped()
        {
            var t = LexSingle("1_2_3_4_5");
            Assert.AreEqual("12345", t.Value);
        }

        [Test]
        public void Lexer_ConsecutiveUnderscores_AllStripped()
        {
            var t = LexSingle("1__000");
            Assert.AreEqual("1000", t.Value);
        }

        [Test]
        public void Lexer_DecimalWithSeparator_Stripped()
        {
            var t = LexSingle("3_141.592_653");
            Assert.AreEqual("3141.592653", t.Value);
        }

        [Test]
        public void Lexer_DecimalPartOnly_Stripped()
        {
            var t = LexSingle("0.001_234");
            Assert.AreEqual("0.001234", t.Value);
        }

        [Test]
        public void Lexer_IntegerPartOnly_WithDecimal_Stripped()
        {
            var t = LexSingle("1_000.5");
            Assert.AreEqual("1000.5", t.Value);
        }

        [Test]
        public void Lexer_SmallNumber_NoSeparator_Unchanged()
        {
            var t = LexSingle("42");
            Assert.AreEqual("42", t.Value);
        }

        [Test]
        public void Lexer_Zero_Unchanged()
        {
            var t = LexSingle("0");
            Assert.AreEqual("0", t.Value);
        }

        [Test]
        public void Lexer_NegativeWithSeparator_Stripped()
        {
            // Negative numbers are lexed as a single token when preceded by operator context
            // Test via expression where - is unambiguously a negative sign
            var tokens = LexicalParser.Parse("a = -1_000");
            var num = tokens.Find(t => t.Type == TokenType.Numeric);
            Assert.IsNotNull(num);
            Assert.AreEqual("-1000", num.Value);
        }

        [Test]
        public void Lexer_TokenType_IsNumeric()
        {
            var t = LexSingle("10_000");
            Assert.AreEqual(TokenType.Numeric, t.Type);
        }

        // ─────────────────────────────────────────────────────────────
        // Execution — values are correct at runtime
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Exec_ThousandsSeparator_CorrectValue()
        {
            Assert.AreEqual(new[] { "1000000" }, Run("print 1_000_000"));
        }

        [Test]
        public void Exec_WithAndWithout_AreEqual()
        {
            Assert.AreEqual(
                Run("print 1000000"),
                Run("print 1_000_000")
            );
        }

        [Test]
        public void Exec_DecimalSeparator_CorrectValue()
        {
            Assert.AreEqual(new[] { "3141.592653" }, Run("print 3_141.592_653"));
        }

        [Test]
        public void Exec_Arithmetic_Addition()
        {
            Assert.AreEqual(new[] { "11000" }, Run("print 1_000 + 10_000"));
        }

        [Test]
        public void Exec_Arithmetic_Subtraction()
        {
            Assert.AreEqual(new[] { "9000" }, Run("print 10_000 - 1_000"));
        }

        [Test]
        public void Exec_Arithmetic_Multiplication()
        {
            Assert.AreEqual(new[] { "1000000" }, Run("print 1_000 * 1_000"));
        }

        [Test]
        public void Exec_Arithmetic_Division()
        {
            Assert.AreEqual(new[] { "100" }, Run("print 1_000_000 / 10_000"));
        }

        [Test]
        public void Exec_Assignment_AndPrint()
        {
            var src = @"
maxHealth = 10_000
print maxHealth";
            Assert.AreEqual(new[] { "10000" }, Run(src));
        }

        [Test]
        public void Exec_Comparison_LessThan()
        {
            var src = @"
gold = 500
if gold < 1_000
  print ""poor""
else
  print ""rich""
end";
            Assert.AreEqual(new[] { "poor" }, Run(src));
        }

        [Test]
        public void Exec_Comparison_GreaterThan()
        {
            var src = @"
score = 1_500_000
if score > 1_000_000
  print ""highscore""
end";
            Assert.AreEqual(new[] { "highscore" }, Run(src));
        }

        [Test]
        public void Exec_InArray_Literal()
        {
            var src = @"
dmgTable = {1_000, 2_500, 10_000}
print dmgTable{0}
print dmgTable{1}
print dmgTable{2}";
            Assert.AreEqual(new[] { "1000", "2500", "10000" }, Run(src));
        }

        [Test]
        public void Exec_InFunctionArg()
        {
            var src = @"
fun double[x]
  return x * 2
end
print double[50_000]";
            Assert.AreEqual(new[] { "100000" }, Run(src));
        }

        [Test]
        public void Exec_InLoopRange()
        {
            // Loop 0..1_000 — upper exclusive, so 0..1_000 = 1000 iterations
            var src = @"
total = 0
loop i in 0..1_000
  total += 1
end
print total";
            Assert.AreEqual(new[] { "1000" }, Run(src));
        }

        [Test]
        public void Exec_CompoundAssignment_WithSeparator()
        {
            var src = @"
gold = 0
gold += 1_500
gold += 2_500
print gold";
            Assert.AreEqual(new[] { "4000" }, Run(src));
        }

        [Test]
        public void Exec_StringInterpolation_WithSeparatedLiteral()
        {
            var src = "print \"damage: {10_000}\"";
            Assert.AreEqual(new[] { "damage: 10000" }, Run(src));
        }

        // ─────────────────────────────────────────────────────────────
        // Game-realistic scenarios
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Game_HealthClamp()
        {
            var src = @"
fun clamp[val, lo, hi]
  if val < lo
    return lo
  end
  if val > hi
    return hi
  end
  return val
end
hp = clamp[99_999, 0, 10_000]
print hp";
            Assert.AreEqual(new[] { "10000" }, Run(src));
        }

        [Test]
        public void Game_GoldThreshold()
        {
            var src = @"
playerGold = 1_234_567
threshold  = 1_000_000
if playerGold >= threshold
  print ""millionaire""
end";
            Assert.AreEqual(new[] { "millionaire" }, Run(src));
        }

        [Test]
        public void Game_DamageConstants()
        {
            var src = @"
CRIT_DAMAGE   = 5_000
NORMAL_DAMAGE = 1_250
CHIP_DAMAGE   = 250

total = CRIT_DAMAGE + NORMAL_DAMAGE + CHIP_DAMAGE
print total";
            Assert.AreEqual(new[] { "6500" }, Run(src));
        }

        [Test]
        public void Game_FloatPrecision_SpeedValue()
        {
            var src = @"
moveSpeed = 3_141.5
print moveSpeed";
            Assert.AreEqual(new[] { "3141.5" }, Run(src));
        }

        // ─────────────────────────────────────────────────────────────
        // No regression on plain numbers
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void Regression_PlainIntegers_Unaffected()
        {
            Assert.AreEqual(new[] { "42" },    Run("print 42"));
            Assert.AreEqual(new[] { "0" },     Run("print 0"));
            Assert.AreEqual(new[] { "999" },   Run("print 999"));
        }

        [Test]
        public void Regression_PlainDecimals_Unaffected()
        {
            Assert.AreEqual(new[] { "3.14" },  Run("print 3.14"));
            Assert.AreEqual(new[] { "0.5" },   Run("print 0.5"));
        }

        [Test]
        public void Regression_NegativeNumbers_Unaffected()
        {
            var src = @"
x = 10
y = x - 5
print y";
            Assert.AreEqual(new[] { "5" }, Run(src));
        }

        [Test]
        public void Regression_RangeOperator_NotConfused()
        {
            // 1..5 must not be tokenized as a number with separators
            var src = @"
total = 0
loop i in 1..5
  total += i
end
print total";
            // 1+2+3+4 = 10 (exclusive upper)
            Assert.AreEqual(new[] { "10" }, Run(src));
        }
    }
}
