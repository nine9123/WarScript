using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Exception;
using WarScript.Token;

namespace Tests
{
    /// <summary>
    /// Columns (v2 plan T3.1): tokens, lexer, parser and compiler
    /// errors, compile diagnostics and the error text's source line with a
    /// caret, and runtime errors' columns. Columns count from 1; a tab is
    /// one column.
    /// </summary>
    [TestFixture]
    public class ColumnTests
    {
        private static Token Find(IReadOnlyList<Token> tokens, string value) =>
            tokens.First(t => t.Value == value);

        private static CompileDiagnostic Diagnose(string source)
        {
            var script = new WarScriptLanguage("col", source, null, null);
            var result = script.Compile();
            Assert.IsFalse(result.Success, "expected a compile error: " + source);
            return result.Diagnostics.Single();
        }

        // ── Tokens ──────────────────────────────────────────────

        [Test]
        public void Tokens_CarryTheColumnOfTheirFirstCharacter()
        {
            var tokens = LexicalParser.Parse("x = foo [12, \"a\"]\n  y :: z");

            Assert.AreEqual(1, Find(tokens, "x").Column);
            Assert.AreEqual(3, Find(tokens, "=").Column);
            Assert.AreEqual(5, Find(tokens, "foo").Column);
            Assert.AreEqual(9, Find(tokens, "[").Column);
            Assert.AreEqual(10, Find(tokens, "12").Column);
            Assert.AreEqual(14, Find(tokens, "a").Column, "a text literal starts at its quote");
            Assert.AreEqual(3, Find(tokens, "y").Column);
            Assert.AreEqual(2, Find(tokens, "y").RowNumber);
            Assert.AreEqual(5, Find(tokens, "::").Column);
            Assert.AreEqual(8, Find(tokens, "z").Column);
        }

        [Test]
        public void ATab_IsOneColumn()
        {
            var tokens = LexicalParser.Parse("\tx =\t1");

            Assert.AreEqual(2, Find(tokens, "x").Column);
            Assert.AreEqual(4, Find(tokens, "=").Column);
            Assert.AreEqual(6, Find(tokens, "1").Column);
        }

        [Test]
        public void Columns_RestartAfterEveryKindOfLineBreak()
        {
            var tokens = LexicalParser.Parse("a\n b\r\n  c\r   d");

            Assert.AreEqual((1, 1), (Find(tokens, "a").RowNumber, Find(tokens, "a").Column));
            Assert.AreEqual((2, 2), (Find(tokens, "b").RowNumber, Find(tokens, "b").Column));
            Assert.AreEqual((3, 3), (Find(tokens, "c").RowNumber, Find(tokens, "c").Column));
            Assert.AreEqual((4, 4), (Find(tokens, "d").RowNumber, Find(tokens, "d").Column));
        }

        [Test]
        public void CrLf_IsOneLineBreak()
        {
            var script = new WarScriptLanguage("crlf", "x = 1\r\ny = 2\r\nz = x / 0\r\n", null, null) { LogUnhandledErrors = false };

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            Assert.AreEqual(3, result.Error.Line, "it was 5: '\\r' and '\\n' each counted a line");
        }

        [Test]
        public void ALineBreakInsideALiteral_RestartsTheColumns()
        {
            var tokens = LexicalParser.Parse("s = \"a\nb\" + c\nt = \"\"\"\nraw\n\"\"\" + d");

            Assert.AreEqual((2, 6), (Find(tokens, "c").RowNumber, Find(tokens, "c").Column));
            Assert.AreEqual((5, 7), (Find(tokens, "d").RowNumber, Find(tokens, "d").Column));
        }

        [Test]
        public void Interpolation_DesugaredTokensCarryTheHolesColumn_TheExpressionKeepsItsOwn()
        {
            //                               1234567890123456789
            var tokens = LexicalParser.Parse("x = \"ab{n + 1}cd\"");

            var open = tokens.First(t => t.Value == "(");
            var close = tokens.First(t => t.Value == ")");
            Assert.AreEqual(8, open.Column, "the '{' of the hole");
            Assert.AreEqual(8, close.Column);
            Assert.AreEqual(9, Find(tokens, "n").Column);
            Assert.AreEqual(13, Find(tokens, "1").Column);
            Assert.AreEqual(5, Find(tokens, "ab").Column, "the first segment: the literal's quote");
            Assert.AreEqual(15, Find(tokens, "cd").Column, "a later segment: its first character");
            Assert.IsTrue(tokens.Where(t => t.Value == "+" && t.Column != 11).All(t => t.Column == 8 || t.Column == 15),
                "the desugared '+' tokens are at the hole or at the segment they join");
        }

        [Test]
        public void Interpolation_OnALaterLine_KeepsTheEnclosingColumns()
        {
            var tokens = LexicalParser.Parse("a = 1\n  print $\"v={a}\"");

            Assert.AreEqual(2, tokens.First(t => t.Value == "(").RowNumber);
            Assert.AreEqual(13, tokens.First(t => t.Value == "(").Column);
            Assert.AreEqual(14, tokens.Last(t => t.Value == "a").Column);
        }

        // ── Lexer errors ────────────────────────────────────────

        [Test]
        public void LexerErrors_HaveALineAndColumn()
        {
            AssertAt("x = 1\ny = 2 ? 3", 2, 7);                 // unexpected character
            AssertAt("x = 1\n  s = \"abc", 2, 7);               // unterminated literal: its quote
            AssertAt("s = \"a\\qb\"", 1, 7);                    // unknown escape: its backslash
            AssertAt("s = \"\"\"never closed", 1, 5);           // unterminated raw literal
            AssertAt("s = $\"\"\"x\"\"\"", 1, 5);               // $""": the '$'
            AssertAt("s = \"a{1 + \"", 1, 5);                    // unterminated interpolation: the literal
            AssertAt("s = \"{1 ? 2}\"", 1, 9);                   // an error inside a hole: its own column
        }

        private static void AssertAt(string source, int line, int column)
        {
            var d = Diagnose(source);
            Assert.AreEqual((line, column), (d.Line, d.Column), source + ": " + d.Message);
            Assert.AreEqual("col", d.ScriptName);
        }

        // ── Parser and compiler errors ──────────────────────────

        [Test]
        public void ParserErrors_AtTheOffendingToken()
        {
            AssertAt("x = 1\nx = 1 2", 2, 7);                    // two operands in a row
            AssertAt("x = 1)", 1, 6);                            // unbalanced ')'
            AssertAt("f [1, ]]\n", 1, 8);                        // a leftover ']'
            AssertAt("x = 1\n   end", 2, 4);                     // leftover at the top level
            AssertAt("x = 1\nimport 5", 2, 8);                    // the token the parser expected another in place of
        }

        [Test]
        public void ParserErrors_WithoutAToken_AtTheStartOfTheirStatement()
        {
            AssertAt("const A = 1\nif true\n    A = 2\nend", 3, 5);  // reassigning a constant
        }

        [Test]
        public void CompilerErrors_AtTheStartOfTheirStatement()
        {
            AssertAt("x = 1\n  break", 2, 3);                    // break outside a loop
            AssertAt("loop i in 0..2\n    1 = 2\nend", 2, 5);   // invalid assignment target
        }

        [Test]
        public void CompilerErrors_InAnExpression_AtItsOperator()
        {
            AssertAt("x = 1\n  y = x as 3", 2, 9);               // 'as' needs a class name (a CompileError)
            AssertAt("x = 1\n  y = (1 + x is 3)", 2, 14);
        }

        [Test]
        public void MalformedExpressions_AtTheirOperator()
        {
            AssertAt("x = 1\n  y = (1", 2, 7);                   // the unclosed '('
            AssertAt("x = 1\n  = 2", 2, 3);                      // an operator without its left operand
        }

        [Test]
        public void SyntaxException_FromEnsureCompiled_CarriesThePosition()
        {
            var script = new WarScriptLanguage("col", "x = 1\n\ty = 2 ? 3", null, null);

            var e = Assert.Throws<SyntaxException>(() => script.Reload("x = 1\n\ty = 2 ? 3"));

            Assert.AreEqual(2, e.Line);
            Assert.AreEqual(8, e.Column);
            Assert.AreEqual("col", e.ScriptName);
        }

        // ── Error text ──────────────────────────────────────────

        [Test]
        public void CompileErrorText_ShowsTheLineAndACaret()
        {
            var script = new WarScriptLanguage("col", "x = 1\n\tif x ?\nend", null, null) { LogUnhandledErrors = false };

            var error = script.Run().Error;

            Assert.AreEqual(ScriptErrorKind.CompileError, error.Kind);
            Assert.AreEqual("\tif x ?", error.SourceLine);
            Assert.AreEqual("col:2:7: CompileError: Unexpected character '?' at line 2\n\tif x ?\n\t     ^", error.ToString(),
                "the caret is indented with the line's own tab");
        }

        [Test]
        public void TheHaltLogLine_CarriesTheExcerpt()
        {
            var log = new List<string>();
            var script = new WarScriptLanguage("col", "x = (1", null, (s, m) => log.Add(m));

            script.Run();

            Assert.AreEqual("[WarScript] " + script.Fault, log.Single());
            StringAssert.EndsWith("\nx = (1\n    ^", log.Single());
        }

        [Test]
        public void ImportErrors_AreReportedInTheImportedFile()
        {
            var files = new Dictionary<string, string> { ["lib"] = "a = 1\n  b = a ? 2\n" };
            var script = new WarScriptLanguage("main", "x = 1\nimport \"lib\"\n", p => files[p], null) { LogUnhandledErrors = false };

            var error = script.Run().Error;

            Assert.AreEqual(ScriptErrorKind.CompileError, error.Kind);
            Assert.AreEqual("lib", error.ScriptName);
            Assert.AreEqual((2, 9), (error.Line, error.Column));
            Assert.AreEqual("  b = a ? 2", error.SourceLine);
        }

        // ── Runtime errors (second half) ────────────────────────

        private static ScriptError RuntimeError(string source)
        {
            var script = new WarScriptLanguage("col", source, null, null) { LogUnhandledErrors = false };
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
            var result = script.Run();
            Assert.AreEqual(ScriptStatus.Error, result.Status, source);
            return result.Error;
        }

        private static void AssertRuntimeAt(string source, int line, int column)
        {
            var error = RuntimeError(source);
            Assert.AreEqual((line, column), (error.Line, error.Column), source + ": " + error.Message);
        }

        [Test]
        public void RuntimeErrors_AtTheirOperator()
        {
            AssertRuntimeAt("x = 1 / 0", 1, 7);
            AssertRuntimeAt("x = 0\ny = 2 + (5 % x)", 2, 12);
            AssertRuntimeAt("y = 1 + true * 2", 1, 14);                   // logical * number
            AssertRuntimeAt("x = 5\ny = x :: hp", 2, 7);                   // property of a non-instance
        }

        [Test]
        public void RuntimeErrors_AtTheirCallOrNew()
        {
            AssertRuntimeAt("x = 1\ny = 2 + missing [x]", 2, 9);         // undefined function
            AssertRuntimeAt("x = new Nope [1]", 1, 9);                    // undefined class: its name
            AssertRuntimeAt("x = 1\n\tsqrt [\"a\"]", 2, 2);               // a native's argument error: the call
        }

        [Test]
        public void RuntimeErrors_WithoutAnExpressionPosition_AtTheirStatement()
        {
            AssertRuntimeAt("x = 1\n   assert x == 2", 2, 4);
            AssertRuntimeAt("x = 1\n  raise \"boom\"", 2, 3);
            AssertRuntimeAt("n = 5\nloop v in n\n  print v\nend", 2, 1);  // iterating a number
        }

        [Test]
        public void ARuntimeError_InAMultiLineExpression_KeepsItsStatementsColumn()
        {
            // The '/' is on line 2 but the statement's line is 1: a column
            // on another line than the one reported would point nowhere.
            AssertRuntimeAt("x = {1,\n  2 / 0}", 1, 1);
        }

        [Test]
        public void RuntimeErrorText_ShowsTheLineAndACaret_AndEveryFrameItsColumn()
        {
            var error = RuntimeError("fun f [a]\n\treturn a / 0\nend\ny = 1 + f [2]");

            Assert.AreEqual("\treturn a / 0", error.SourceLine);
            Assert.AreEqual("col:2:11: DivisionByZero: Division by zero\n\treturn a / 0\n\t         ^\n"
                            + "  at f (col:2:11)\n  at <script> (col:4:9)", error.ToString());
        }

        [Test]
        public void RuntimeErrorsInAnImport_HaveAColumnButNoExcerpt()
        {
            var files = new Dictionary<string, string> { ["lib"] = "a = 1\n  b = a / 0\n" };
            var script = new WarScriptLanguage("main", "x = 1\nimport \"lib\"\n", p => files[p], null) { LogUnhandledErrors = false };

            var error = script.Run().Error;

            Assert.AreEqual("lib", error.ScriptName);
            Assert.AreEqual((2, 9), (error.Line, error.Column));
            Assert.IsNull(error.SourceLine, "the instance keeps only its own source");
        }

        [Test]
        public void RuntimeErrors_FromLoadedBytecode_HaveNoColumn()
        {
            var compiled = new WarScriptLanguage("col", "x = 1\ny = x / 0", null, null);
            var bytes = new System.IO.MemoryStream();
            compiled.SaveBytecode(bytes);
            var loaded = new WarScriptLanguage("col", "", null, null) { LogUnhandledErrors = false };
            bytes.Position = 0;
            loaded.LoadBytecode(bytes);

            var error = loaded.Run().Error;

            Assert.AreEqual((2, 0), (error.Line, error.Column), "the file's line table has no columns until format v3 (T3.11)");
            Assert.IsNull(error.SourceLine);
        }

        [Test]
        public void LineTable_AddsAnEntryWhenTheColumnChanges()
        {
            var lines = new WarScript.Bytecode.LineTable();
            lines.Mark(0, 1, 1);
            lines.Mark(1, 1, 1);
            lines.Mark(2, 1, 7);
            lines.Mark(3, 2, 7);

            Assert.AreEqual(3, lines.EntryCount);
            Assert.AreEqual((1, 1), (lines.GetLine(1), lines.GetColumn(1)));
            Assert.AreEqual((1, 7), (lines.GetLine(2), lines.GetColumn(2)));
            Assert.AreEqual((2, 7), (lines.GetLine(3), lines.GetColumn(3)));
        }

        [Test]
        public void ALoneCarriageReturn_InsideALiteral_EndsALine()
        {
            // The excerpt counts '\r' as a line break; so must the lexer, in
            // literals too, or the excerpt shows another line.
            var d = Diagnose("x = 1\ry = \"a\rb\"\rz = )\n");

            Assert.AreEqual(4, d.Line);
            var tokens = LexicalParser.Parse("s = \"\"\"a\rb\"\"\"\rt = \"{1 +\r2}\"\ru = 1");
            Assert.AreEqual(3, Find(tokens, "t").RowNumber);
            Assert.AreEqual(5, Find(tokens, "u").RowNumber);
        }

        [Test]
        public void SourceLineOf_CountsLinesAsTheLexerDoes()
        {
            Assert.AreEqual("a", ScriptError.SourceLineOf("a\r\nb\rc\nd", 1));
            Assert.AreEqual("b", ScriptError.SourceLineOf("a\r\nb\rc\nd", 2));
            Assert.AreEqual("c", ScriptError.SourceLineOf("a\r\nb\rc\nd", 3));
            Assert.AreEqual("d", ScriptError.SourceLineOf("a\r\nb\rc\nd", 4));
            Assert.IsNull(ScriptError.SourceLineOf("a\nb", 3));
            Assert.AreEqual("", ScriptError.SourceLineOf("a\n", 2));
            Assert.IsNull(ScriptError.SourceLineOf("a", 0));
        }
    }
}
