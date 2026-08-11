using NUnit.Framework;
using WarScript.Exception;

namespace Tests
{
    /// <summary>
    /// Malformed source must fail with a SyntaxException — never hang, never
    /// crash the process, and never silently drop part of the program.
    ///
    /// Several of these are regression tests for parser/compiler bugs:
    ///   - unterminated literals/argument lists used to spin forever
    ///   - an invalid assignment target used to recurse until StackOverflow
    ///     (killing the host process)
    ///   - a statement the parser could not start used to silently truncate
    ///     the rest of the script
    ///   - an unbalanced ')' used to escape as InvalidOperationException
    /// </summary>
    [TestFixture]
    public class SyntaxErrorTests
    {
        private static SyntaxException AssertSyntaxError(string source)
        {
            return Assert.Throws<SyntaxException>(() => TestHelper.Run("syntax_error", source));
        }

        // ────────────────────────────────────────────────
        //  Lexer errors
        // ────────────────────────────────────────────────

        [Test] public void UnexpectedCharacter_At() => AssertSyntaxError("x = 1 @ 2");
        [Test] public void UnexpectedCharacter_Amp() => AssertSyntaxError("x = 1 & 2");
        [Test] public void UnexpectedCharacter_Semicolon() => AssertSyntaxError("x = 1;");
        [Test] public void UnexpectedCharacter_Question() => AssertSyntaxError("x = a ? b");
        [Test] public void UnexpectedCharacter_Caret() => AssertSyntaxError("x = 2 ^ 3");

        [Test]
        public void UnexpectedCharacter_ReportsLineNumber()
        {
            var ex = AssertSyntaxError("x = 1\ny = 2\nz = 3 @ 4");
            StringAssert.Contains("line 3", ex.Message);
        }

        // ────────────────────────────────────────────────
        //  Unterminated constructs (used to hang the parser)
        // ────────────────────────────────────────────────

        [Test, Timeout(10000)]
        public void UnterminatedArrayLiteral_Throws() => AssertSyntaxError("x = {1, 2");

        [Test, Timeout(10000)]
        public void UnterminatedArrayLiteral_WithComma_Throws() => AssertSyntaxError("x = {1, 2,");

        [Test, Timeout(10000)]
        public void UnterminatedCallArguments_Throws() =>
            AssertSyntaxError("fun f [a] return a end\nx = f [1");

        [Test, Timeout(10000)]
        public void UnterminatedCallArguments_AfterComma_Throws() =>
            AssertSyntaxError("fun f [a, b] return a end\nx = f [1,");

        [Test, Timeout(10000)]
        public void UnterminatedConstructorArguments_Throws() =>
            AssertSyntaxError("class P [x] end\np = new P [1");

        [Test]
        public void UnterminatedFunctionParameterList_Throws() =>
            AssertSyntaxError("fun f [a, b");

        [Test]
        public void UnterminatedClassParameterList_Throws() =>
            AssertSyntaxError("class P [x");

        // ────────────────────────────────────────────────
        //  Missing `end`
        // ────────────────────────────────────────────────

        [Test] public void MissingEnd_If() => AssertSyntaxError("if 1 == 1\nprint 1");
        [Test] public void MissingEnd_Function() => AssertSyntaxError("fun f []\nreturn 1");
        [Test] public void MissingEnd_Loop() => AssertSyntaxError("loop i in 0..3\nprint i");
        [Test] public void MissingEnd_Class() => AssertSyntaxError("class P [x]\nfun m [] return 1 end");
        [Test] public void MissingEnd_Begin() => AssertSyntaxError("begin\nraise \"x\"\nrescue e\nprint e");
        [Test] public void MissingEnd_Enum() => AssertSyntaxError("enum E\nA\nB");

        // ────────────────────────────────────────────────
        //  Stray / misplaced keywords no statement can start with.
        //  These used to silently truncate the rest of the script.
        // ────────────────────────────────────────────────

        [Test]
        public void StrayEnd_IsAnError_NotSilentTruncation() =>
            AssertSyntaxError("print 1\nend\nprint 2");

        [Test]
        public void DanglingElse_IsAnError() => AssertSyntaxError("else\nprint 1\nend");

        [Test]
        public void DanglingElif_IsAnError() => AssertSyntaxError("elif 1 == 1\nprint 1\nend");

        [Test]
        public void DanglingRescue_IsAnError() => AssertSyntaxError("rescue e\nprint e\nend");

        [Test]
        public void DanglingEnsure_IsAnError() => AssertSyntaxError("ensure\nprint 1\nend");

        [Test]
        public void StatementStartingWithNumber_IsAnError() => AssertSyntaxError("1 = 2");

        [Test]
        public void StatementStartingWithString_IsAnError() => AssertSyntaxError("\"abc\"\nprint 1");

        [Test]
        public void StrayEnd_ErrorNamesTheTokenAndLine()
        {
            var ex = AssertSyntaxError("print 1\nend");
            StringAssert.Contains("end", ex.Message);
            StringAssert.Contains("line 2", ex.Message);
        }

        // ────────────────────────────────────────────────
        //  break / next placement
        // ────────────────────────────────────────────────

        [Test] public void BreakOutsideLoop_TopLevel() => AssertSyntaxError("break");
        [Test] public void NextOutsideLoop_TopLevel() => AssertSyntaxError("next");
        [Test] public void BreakOutsideLoop_InFunction() =>
            AssertSyntaxError("fun f []\nbreak\nend\nf []");
        [Test] public void BreakOutsideLoop_InIf() =>
            AssertSyntaxError("if 1 == 1\nbreak\nend");

        [Test]
        public void BreakInsideIfInsideLoop_IsValid()
        {
            var (_, output) = TestHelper.Run("break_ok",
                "n = 0\nloop i in 0..10\nif i == 3\nbreak\nend\nn += 1\nend\nprint n");
            Assert.AreEqual("3", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Malformed expressions
        // ────────────────────────────────────────────────

        [Test] public void UnbalancedRightParen_Throws() => AssertSyntaxError("x = 1)");
        [Test] public void UnbalancedRightParen_AfterExpr_Throws() => AssertSyntaxError("x = (1 + 2))");
        [Test] public void BinaryOperatorMissingRightOperand_Throws() => AssertSyntaxError("x = 1 +");
        [Test] public void BinaryOperatorMissingLeftOperand_Throws() => AssertSyntaxError("x = * 5");
        [Test] public void TwoConsecutiveOperands_Throws() => AssertSyntaxError("x = 1 2");
        [Test] public void TwoConsecutiveOperands_Print_Throws() => AssertSyntaxError("print 1 2");
        [Test] public void TwoConsecutiveOperands_Variables_Throws() => AssertSyntaxError("a = 1\nb = 2\nx = a b");
        [Test] public void DoubleDecimalPoint_Throws() => AssertSyntaxError("x = 1.2.3");

        // ────────────────────────────────────────────────
        //  Invalid assignment targets.
        //  These used to recurse infinitely in the compiler and kill the
        //  process with a StackOverflow.
        // ────────────────────────────────────────────────

        [Test]
        public void AssignToFunctionCall_Throws() =>
            AssertSyntaxError("fun f [] return 1 end\nf [] = 5");

        [Test]
        public void AssignToFunctionCallWithArg_Throws() =>
            AssertSyntaxError("fun f [a] return a end\nf [1] = 5");

        [Test]
        public void ChainedAssignment_Throws() =>
            AssertSyntaxError("y = 0\nx = y = 1");

        [Test]
        public void AssignToArithmeticResult_Throws() =>
            AssertSyntaxError("a = 1\na + 1 = 2");

        // ────────────────────────────────────────────────
        //  loop statement errors
        // ────────────────────────────────────────────────

        [Test]
        public void LoopWithoutCondition_Throws() => AssertSyntaxError("loop\nprint 1\nend");

        // ────────────────────────────────────────────────
        //  Numeric literal errors surfaced at parse time
        // ────────────────────────────────────────────────

        [Test]
        public void IntegerPartOutOfRange_Throws() => AssertSyntaxError("x = 2147483648");

        [Test]
        public void HugeLiteral_Throws() => AssertSyntaxError("x = 99999999999999999999");

        [Test]
        public void MaxIntegerLiteral_Parses()
        {
            var (_, output) = TestHelper.Run("max_int", "x = 2147483647\nprint x");
            Assert.AreEqual("2147483647", output[0]);
        }

        [Test]
        public void MinIntegerLiteral_Parses()
        {
            var (_, output) = TestHelper.Run("min_int", "x = -2147483647\nprint x");
            Assert.AreEqual("-2147483647", output[0]);
        }

        // ────────────────────────────────────────────────
        //  import syntax
        // ────────────────────────────────────────────────

        [Test]
        public void ImportWithoutPath_Throws() => AssertSyntaxError("import 42");

        [Test]
        public void ImportWithIdentifier_Throws() => AssertSyntaxError("import lib");

        // ────────────────────────────────────────────────
        //  Default parameter placement
        // ────────────────────────────────────────────────

        [Test]
        public void RequiredParamAfterDefault_Throws() =>
            AssertSyntaxError("fun f [a = 1, b]\nreturn a\nend");

        // ────────────────────────────────────────────────
        //  Enum syntax
        // ────────────────────────────────────────────────

        [Test]
        public void EnumValueMustBeNumericLiteral_Expression_Throws() =>
            AssertSyntaxError("enum E\nA = 1 + 2\nend");

        [Test]
        public void EnumValueMustBeNumericLiteral_String_Throws() =>
            AssertSyntaxError("enum E\nA = \"x\"\nend");
    }
}
