using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Token;

namespace Tests
{
    /// <summary>
    /// Escape sequences, the explicit <c>$"..."</c> interpolation prefix and
    /// raw <c>"""..."""</c> literals — the three ways a text literal can carry
    /// characters that used to terminate it.
    /// </summary>
    [TestFixture]
    public class StringLiteralTests
    {
        // ── Escape sequences ──

        [Test]
        public void EscapedQuoteIsLiteral()
        {
            var (_, output) = TestHelper.Run("test", @"
                print ""she said \""hi\"" once""
            ");
            Assert.AreEqual(new[] { "she said \"hi\" once" }, output);
        }

        [Test]
        public void EscapedBackslashIsLiteral()
        {
            var (_, output) = TestHelper.Run("test", @"
                print ""a\\b""
            ");
            Assert.AreEqual(new[] { "a\\b" }, output);
        }

        [Test]
        public void EscapedBraceSuppressesInterpolation()
        {
            var (_, output) = TestHelper.Run("test", @"
                name = ""hero""
                print ""\{name\} stays literal, {name} does not""
            ");
            Assert.AreEqual(new[] { "{name} stays literal, hero does not" }, output);
        }

        [Test]
        public void EscapedControlCharacters()
        {
            var (_, output) = TestHelper.Run("test", @"
                print ""a\nb\tc\rd""
            ");
            Assert.AreEqual(new[] { "a\nb\tc\rd" }, output);
        }

        [Test]
        public void EscapesWorkInsideInterpolatedSegments()
        {
            var (_, output) = TestHelper.Run("test", @"
                n = 3
                print ""\""{n}\""""
            ");
            Assert.AreEqual(new[] { "\"3\"" }, output);
        }

        [Test]
        public void UnknownEscapeThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    print ""bad \q escape""
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void UnterminatedLiteralThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    print ""no closing quote
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void UnterminatedInterpolationThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", @"
                    print ""value: {x
                ");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        // ── $"..." explicit interpolation prefix ──

        [Test]
        public void DollarPrefixInterpolates()
        {
            var (_, output) = TestHelper.Run("test", @"
                name = ""hero""
                print $""Hello {name}""
            ");
            Assert.AreEqual(new[] { "Hello hero" }, output);
        }

        [Test]
        public void DollarPrefixMatchesBareLiteral()
        {
            var (_, output) = TestHelper.Run("test", @"
                a = 2
                b = 3
                print $""{a} + {b} = {a + b}"" == ""{a} + {b} = {a + b}""
            ");
            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void DollarPrefixOnRawLiteralThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", "print $\"\"\"raw\"\"\"");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        // ── Raw """...""" literals ──

        [Test]
        public void RawLiteralKeepsQuotesAndBraces()
        {
            var (_, output) = TestHelper.Run("test", "name = \"hero\"\nprint \"\"\"say \"{name}\" now\"\"\"");
            Assert.AreEqual(new[] { "say \"{name}\" now" }, output);
        }

        [Test]
        public void RawLiteralDoesNotProcessEscapes()
        {
            var (_, output) = TestHelper.Run("test", "print \"\"\"a\\nb\"\"\"");
            Assert.AreEqual(new[] { "a\\nb" }, output);
        }

        [Test]
        public void RawLiteralSpansLinesAndTrimsTheDelimiterLines()
        {
            var source = string.Join("\n",
                "text = \"\"\"",
                "line one",
                "line two",
                "\"\"\"",
                "print text");
            var (_, output) = TestHelper.Run("test", source);
            Assert.AreEqual(new[] { "line one\nline two" }, output);
        }

        [Test]
        public void RawLiteralOnOneLineKeepsItsContentExactly()
        {
            var (_, output) = TestHelper.Run("test", "print \"\"\"  spaced  \"\"\"");
            Assert.AreEqual(new[] { "  spaced  " }, output);
        }

        [Test]
        public void RawLiteralMayEndInAQuote()
        {
            var (_, output) = TestHelper.Run("test", "print \"\"\"say \"hi\"\"\"\"");
            Assert.AreEqual(new[] { "say \"hi\"" }, output);
        }

        [Test]
        public void EmptyRawLiteral()
        {
            var (_, output) = TestHelper.Run("test", "print \"\"\"\"\"\" == \"\"");
            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void RawLiteralHoldingOnlyItsDelimiterLinesIsEmpty()
        {
            var (_, output) = TestHelper.Run("test", "text = \"\"\"\n    \"\"\"\nprint text == \"\"");
            Assert.AreEqual(new[] { "True" }, output);
        }

        [Test]
        public void UnterminatedRawLiteralThrows()
        {
            Assert.That(() =>
            {
                TestHelper.Run("test", "text = \"\"\"never closed");
            }, Throws.TypeOf<WarScript.Exception.SyntaxException>());
        }

        [Test]
        public void RawLiteralKeepsRowNumbersInStep()
        {
            var tokens = LexicalParser.Parse("a = \"\"\"\nx\ny\n\"\"\"\nb = 1");
            var last = tokens[tokens.Count - 1];
            Assert.AreEqual(TokenType.Numeric, last.Type);
            Assert.AreEqual("1", last.Value);
            Assert.AreEqual(5, last.RowNumber);
        }

        [Test]
        public void MultiLineInterpolationKeepsRowNumbersInStep()
        {
            // Line breaks consumed inside {...} and inside a literal nested in
            // it still have to reach the row counter, or every later statement
            // reports the wrong line to the debugger.
            AssertLastTokenRow("a = \"{\n1\n}\"\nb = 2", 4);
            AssertLastTokenRow("a = \"{ \"x\ny\" }\"\nb = 2", 3);
            AssertLastTokenRow("a = \"x\ny\"\nb = 2", 3);
        }

        private static void AssertLastTokenRow(string source, int expectedRow)
        {
            var tokens = LexicalParser.Parse(source);
            var last = tokens[tokens.Count - 1];
            Assert.AreEqual(TokenType.Numeric, last.Type);
            Assert.AreEqual(expectedRow, last.RowNumber, $"row of '{last.Value}' in:\n{source}");
        }

        // ── Nested literals inside an interpolation ──

        [Test]
        public void BraceInsideNestedLiteralDoesNotCloseTheInterpolation()
        {
            var (_, output) = TestHelper.Run("test", @"
                print ""result: {""a}b""}""
            ");
            Assert.AreEqual(new[] { "result: a}b" }, output);
        }

        [Test]
        public void EscapedQuoteInsideNestedLiteral()
        {
            var (_, output) = TestHelper.Run("test", @"
                print ""{""\""quoted\""""}""
            ");
            Assert.AreEqual(new[] { "\"quoted\"" }, output);
        }

        // ── The motivating case: WarScript source carried as a value ──

        [Test]
        public void RawLiteralCarriesWarScriptSourceThatStillRuns()
        {
            var source = string.Join("\n",
                "snippet = \"\"\"",
                "greeting = \"hello\"",
                "party = {1, 2, 3}",
                "print \"{greeting}, {party{0}} of {party{2}}\"",
                "\"\"\"",
                "print snippet");

            var (_, carried) = TestHelper.Run("outer", source);
            Assert.AreEqual(1, carried.Count);

            // The captured text is the inner script verbatim — run it as one.
            var (_, inner) = TestHelper.Run("inner", carried[0]);
            Assert.AreEqual(new List<string> { "hello, 1 of 3" }, inner);
        }

        [Test]
        public void RawLiteralSourceCanBePassedThroughAFunction()
        {
            var (_, output) = TestHelper.Run("test", string.Join("\n",
                "fun option [label, action]",
                "    return label + \" -> \" + action",
                "end",
                "print option [\"Ask about the war\", \"\"\"npc_say [\"It ended.\"]\"\"\"]"));

            Assert.AreEqual(new[] { "Ask about the war -> npc_say [\"It ended.\"]" }, output);
        }

        [Test]
        public void StringLiteralScript()
        {
            var (script, output) = TestHelper.RunFile("test_string_literals.ws");

            Assert.IsFalse(script.ExceptionContext.IsRaised(),
                $"Script raised an unhandled exception. Output:\n{string.Join("\n", output)}");
            Assert.IsEmpty(output,
                $"Script produced unexpected output:\n{string.Join("\n", output)}");
        }
    }
}
