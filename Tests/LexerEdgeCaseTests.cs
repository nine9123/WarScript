using System.Collections.Generic;
using NUnit.Framework;
using WarScript.Exception;

namespace Tests
{
    /// <summary>
    /// Lexer-level edge cases with no previous coverage: numeric separator
    /// placement, dot-adjacent literals, line-ending and whitespace variants,
    /// unicode handling, comments containing string syntax, and definition
    /// shadowing. Everything here pins current deterministic behavior so an
    /// accidental lexer change shows up as a test failure.
    /// </summary>
    [TestFixture]
    public class LexerEdgeCaseTests
    {
        private static List<string> Run(string source)
        {
            var (_, output) = TestHelper.Run("lexer_edge", source);
            return output;
        }

        // ────────────────────────────────────────────────
        //  Numeric literal shapes
        // ────────────────────────────────────────────────

        [Test]
        public void TrailingDecimalPoint_ParsesAsInteger()
        {
            Assert.AreEqual("1", Run("x = 1.\nprint x")[0]);
        }

        [Test]
        public void LeadingDecimalPoint_ParsesAsFraction()
        {
            Assert.AreEqual("0.5", Run("x = .5\nprint x")[0]);
        }

        [Test]
        public void Separator_AfterDecimalPoint_IsStripped()
        {
            Assert.AreEqual("1.5", Run("x = 1._5\nprint x")[0]);
        }

        [Test]
        public void Separator_BeforeDecimalPoint_IsStripped()
        {
            Assert.AreEqual("1.5", Run("x = 1_.5\nprint x")[0]);
        }

        [Test]
        public void TrailingSeparator_IsStripped()
        {
            Assert.AreEqual("1", Run("x = 1_\nprint x")[0]);
        }

        [Test]
        public void LeadingUnderscore_IsAnIdentifier_NotANumber()
        {
            Assert.AreEqual("7", Run("_1 = 7\nprint _1")[0]);
        }

        // ────────────────────────────────────────────────
        //  Whitespace and line endings
        // ────────────────────────────────────────────────

        [Test]
        public void CrLfLineEndings_Work()
        {
            Assert.AreEqual("3", Run("x = 1\r\ny = 2\r\nprint x + y")[0]);
        }

        [Test]
        public void Tabs_AreWhitespace()
        {
            Assert.AreEqual("5", Run("\tx\t=\t5\n\tprint\tx")[0]);
        }

        // ────────────────────────────────────────────────
        //  Unicode
        // ────────────────────────────────────────────────

        [Test]
        public void UnicodeInStringLiteral_PassesThrough()
        {
            Assert.AreEqual("héllo", Run("print \"héllo\"")[0]);
        }

        [Test]
        public void UnicodeIdentifier_IsRejected()
        {
            var ex = Assert.Throws<SyntaxException>(() => TestHelper.Run("uni", "é = 1"));
            StringAssert.Contains("Unexpected character", ex.Message);
        }

        // ────────────────────────────────────────────────
        //  Comments containing string syntax
        // ────────────────────────────────────────────────

        [Test]
        public void CommentContainingQuotesAndBraces_IsIgnored()
        {
            Assert.AreEqual("1", Run("# comment with \"\"\" and { stuff\nprint 1")[0]);
        }

        // ────────────────────────────────────────────────
        //  Raw literal limits
        // ────────────────────────────────────────────────

        [Test]
        public void RawLiteral_CannotContainTripleQuoteRun()
        {
            // """a"""b""" closes after `a`, leaving an unterminated raw
            // literal behind — a clean SyntaxException either way.
            Assert.Throws<SyntaxException>(
                () => TestHelper.Run("raw", "x = \"\"\"a\"\"\"b\"\"\"\nprint x"));
        }

        [Test]
        public void EscapedQuoteAtEndOfLiteral_IsUnterminated()
        {
            var ex = Assert.Throws<SyntaxException>(
                () => TestHelper.Run("esc", "x = \"abc\\\""));
            StringAssert.Contains("Unterminated", ex.Message);
        }

        // ────────────────────────────────────────────────
        //  Interpolation nesting
        // ────────────────────────────────────────────────

        [Test]
        public void Interpolation_ContainingQuotedStrings()
        {
            Assert.AreEqual("a bc d", Run("print \"a {\"b\" + \"c\"} d\"")[0]);
        }

        [Test]
        public void Interpolation_ContainingRawString()
        {
            Assert.AreEqual("v q5", Run("x = 5\nprint \"v { \"\"\"q\"\"\" + x }\"")[0]);
        }

        [Test]
        public void EmptyInterpolation_IsSyntaxError()
        {
            Assert.Throws<SyntaxException>(() => TestHelper.Run("interp", "print \"a{}b\""));
        }

        [Test]
        public void EmptyParentheses_AreSyntaxError()
        {
            Assert.Throws<SyntaxException>(() => TestHelper.Run("paren", "x = ()"));
        }

        // ────────────────────────────────────────────────
        //  Default parameters — expression edges
        // ────────────────────────────────────────────────

        [Test]
        public void DefaultParameter_CanReferenceEarlierParameter()
        {
            Assert.AreEqual("7", Run("fun f [a, b = a]\nreturn b\nend\nprint f [7]")[0]);
        }

        [Test]
        public void LambdaParameters_CannotHaveDefaults()
        {
            Assert.Throws<SyntaxException>(
                () => TestHelper.Run("lam", "f = fun [x = 1] return x end\nprint f [2]"));
        }

        // ────────────────────────────────────────────────
        //  Postfix call on an indexed value (documented limitation)
        // ────────────────────────────────────────────────

        [Test]
        public void PostfixCallOnIndexedValue_IsSyntaxError()
        {
            // arr{0}[args] is documented as unsupported — must be a clean
            // error, not a silently ignored trailing token.
            Assert.Throws<SyntaxException>(
                () => TestHelper.Run("postfix", "arr = {1}\nx = arr{0} [5]\nprint x"));
        }

        // ────────────────────────────────────────────────
        //  Definition shadowing (last definition wins)
        // ────────────────────────────────────────────────

        [Test]
        public void DuplicateFunction_SameArity_LastDefinitionWins()
        {
            Assert.AreEqual("2", Run("fun f [] return 1 end\nfun f [] return 2 end\nprint f []")[0]);
        }

        [Test]
        public void DuplicateClass_LastDefinitionWins()
        {
            var output = Run("class C [x] end\nclass C [y] end\nc = new C [1]\nprint c :: y");
            Assert.AreEqual("1", output[0]);
        }
    }
}
