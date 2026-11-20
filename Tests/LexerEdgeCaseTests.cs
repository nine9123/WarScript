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
        //  Duplicate definitions (PROP-12: a compile error, T3.7)
        // ────────────────────────────────────────────────

        [Test]
        public void DuplicateFunction_SameArity_IsASyntaxErrorAtTheSecond()
        {
            var ex = Assert.Throws<SyntaxException>(
                () => Run("fun f [] return 1 end\nfun f [] return 2 end\nprint f []"));
            StringAssert.Contains("Function 'f' with 0 arguments is already defined at line 1", ex.Message);
            Assert.AreEqual(2, ex.Line);
            Assert.AreEqual(5, ex.Column);
        }

        [Test]
        public void DuplicateClass_IsASyntaxErrorAtTheSecond()
        {
            var ex = Assert.Throws<SyntaxException>(
                () => Run("class C [x] end\nclass C [y] end\nc = new C [1]\nprint c :: y"));
            StringAssert.Contains("Class 'C' is already defined at line 1", ex.Message);
            Assert.AreEqual(2, ex.Line);
        }

        [Test]
        public void DuplicateEnumAndClass_IsASyntaxError()
        {
            var ex = Assert.Throws<SyntaxException>(() => Run("class E end\nenum E\n    A\nend"));
            StringAssert.Contains("Class 'E' is already defined at line 1", ex.Message);
        }

        [TestCase("fun f [a, b = 1] return 1 end\nfun f [a] return 2 end", "Function 'f' with 1 argument is already defined")]
        [TestCase("fun f [a] return 1 end\nfun f [a, b = 1] return 2 end", "Function 'f' with 1 argument is already defined")]
        [TestCase("class C\n    fun m [] return 1 end\n    fun m [] return 2 end\nend", "Function 'm' with 0 arguments is already defined")]
        public void DuplicateDefinition_OverlappingArityOrMethod_IsASyntaxError(string source, string message)
        {
            var ex = Assert.Throws<SyntaxException>(() => Run(source));
            StringAssert.Contains(message, ex.Message);
        }

        [Test]
        public void SameName_DifferentArities_ClassesAndMethods_AreNotDuplicates()
        {
            var output = Run(
                "fun f [] return 0 end\n" +
                "fun f [a] return a end\n" +
                "fun f [a, b, c = 3] return a + b + c end\n" +
                "class A\n    fun m [] return \"A\" end\nend\n" +
                "class B\n    fun m [] return \"B\" end\nend\n" +
                "class f end\n" +
                "print f []\nprint f [5]\nprint f [1, 2]\na = new A\nb = new B\nprint a :: m []\nprint b :: m []");
            CollectionAssert.AreEqual(new[] { "0", "5", "6", "A", "B" }, output);
        }
    }
}

