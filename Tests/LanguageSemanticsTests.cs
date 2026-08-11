using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Pins down language semantics that were previously untested: truthiness
    /// of every value tag, cross-type operator behavior, out-of-bounds access,
    /// negative-literal disambiguation, loop condition forms, runtime error
    /// messages, and VM resource-limit behavior.
    ///
    /// Includes regression tests for fixed bugs:
    ///   - `(1+2)-3` lexed `-3` as a literal and silently discarded `(1+2)`
    ///   - `loop f[]` / `loop true` silently dropped the loop and truncated
    ///     the rest of the script
    ///   - `this` outside a class crashed with InvalidOperationException
    ///   - value-stack / handler-stack overflow crashed with
    ///     IndexOutOfRangeException instead of raising a script error
    /// </summary>
    [TestFixture]
    public class LanguageSemanticsTests
    {
        private static List<string> Run(string source)
        {
            var (_, output) = TestHelper.Run("semantics", source);
            return output;
        }

        // ────────────────────────────────────────────────
        //  Negative-literal disambiguation (regression)
        // ────────────────────────────────────────────────

        [Test]
        public void MinusAfterClosingParen_IsSubtraction()
        {
            Assert.AreEqual("0", Run("x = (1 + 2)-3\nprint x")[0]);
        }

        [Test]
        public void MinusAfterArrayIndex_IsSubtraction()
        {
            Assert.AreEqual("9", Run("arr = {10, 20}\nx = arr{0} -1\nprint x")[0]);
        }

        [Test]
        public void MinusAfterFunctionCall_IsSubtraction()
        {
            Assert.AreEqual("9", Run("fun f [] return 10 end\nx = f [] -1\nprint x")[0]);
        }

        [Test]
        public void MinusAfterVariable_IsSubtraction()
        {
            Assert.AreEqual("4", Run("a = 5\nprint a -1")[0]);
        }

        [Test]
        public void MinusAtExpressionStart_IsNegativeLiteral()
        {
            Assert.AreEqual("-5", Run("x = -5\nprint x")[0]);
        }

        [Test]
        public void MinusAfterOperator_IsNegativeLiteral()
        {
            Assert.AreEqual("6", Run("print 1 - -5")[0]);
        }

        [Test]
        public void MinusInsideCallArguments_IsNegativeLiteral()
        {
            Assert.AreEqual("-3", Run("fun id [v] return v end\nprint id [-3]")[0]);
        }

        [Test]
        public void MinusInsideArrayLiteral_IsNegativeLiteral()
        {
            Assert.AreEqual("-7", Run("arr = {-7}\nprint arr{0}")[0]);
        }

        [Test]
        public void DoubleUnaryMinus()
        {
            Assert.AreEqual("5", Run("print - -5")[0]);
        }

        [Test]
        public void NegativeIndexExpression_InArrayBraces()
        {
            // arr{-1} is out of bounds and reads as null
            Assert.AreEqual("null", Run("arr = {1, 2}\nprint arr{-1}")[0]);
        }

        // ────────────────────────────────────────────────
        //  Loop condition forms (regression: these were silently dropped)
        // ────────────────────────────────────────────────

        [Test]
        public void WhileLoop_OnFunctionCallCondition()
        {
            var output = Run(
                "n = 0\n" +
                "fun check [] return n < 3 end\n" +
                "loop check []\n" +
                "n += 1\n" +
                "end\n" +
                "print n");
            Assert.AreEqual("3", output[0]);
        }

        [Test]
        public void WhileLoop_OnTrueLiteral_WithBreak()
        {
            var output = Run(
                "n = 0\n" +
                "loop true\n" +
                "n += 1\n" +
                "if n == 3\nbreak\nend\n" +
                "end\n" +
                "print n");
            Assert.AreEqual("3", output[0]);
        }

        [Test]
        public void WhileLoop_OnFalseLiteral_NeverRuns()
        {
            var output = Run("n = 0\nloop false\nn += 1\nend\nprint n");
            Assert.AreEqual("0", output[0]);
        }

        [Test]
        public void WhileLoop_WithNext()
        {
            // 1+2+4+5 (3 skipped)
            var output = Run(
                "n = 0\nsum = 0\n" +
                "loop n < 5\n" +
                "n += 1\n" +
                "if n == 3\nnext\nend\n" +
                "sum += n\n" +
                "end\n" +
                "print sum");
            Assert.AreEqual("12", output[0]);
        }

        [Test]
        public void ForLoop_DescendingRange_RunsZeroTimes()
        {
            // The for-range condition is strictly `i < limit`, so a descending
            // range never enters the body — pinned behavior, not an accident.
            var output = Run("n = 0\nloop i in 10..0 by -1\nn += 1\nend\nprint n");
            Assert.AreEqual("0", output[0]);
        }

        [Test]
        public void ForLoop_UpperBoundIsExclusive()
        {
            var output = Run("last = -1\nloop i in 0..5\nlast = i\nend\nprint last");
            Assert.AreEqual("4", output[0]);
        }

        [Test]
        public void ForLoop_FractionalStep()
        {
            var output = Run("n = 0\nloop i in 0..2 by 0.5\nn += 1\nend\nprint n");
            Assert.AreEqual("4", output[0]);
        }

        [Test]
        public void ForeachOverString_IsRuntimeError_Catchable()
        {
            var output = Run(
                "begin\n" +
                "loop c in \"ab\"\nprint c\nend\n" +
                "rescue e\n" +
                "print \"err: \" + e\n" +
                "end");
            Assert.AreEqual("err: Unable to iterate 'ab'", output[0]);
        }

        [Test]
        public void ForeachOverNumber_IsRuntimeError_Catchable()
        {
            var output = Run(
                "begin\nloop c in 5\nprint c\nend\nrescue e\nprint \"caught\"\nend");
            Assert.AreEqual("caught", output[0]);
        }

        [Test]
        public void ForeachOverClassInstance_IteratesPropertyValues()
        {
            var output = Run(
                "class P [x, y] end\n" +
                "p = new P [7, 8]\n" +
                "loop v in p\nprint v\nend");
            Assert.AreEqual(new[] { "7", "8" }, output);
        }

        // ────────────────────────────────────────────────
        //  Truthiness of every value tag
        // ────────────────────────────────────────────────

        private static string Truthy(string expr) =>
            Run("if " + expr + "\nprint \"T\"\nelse\nprint \"F\"\nend")[0];

        [Test] public void Truthiness_True() => Assert.AreEqual("T", Truthy("true"));
        [Test] public void Truthiness_False() => Assert.AreEqual("F", Truthy("false"));
        [Test] public void Truthiness_NonZeroNumber() => Assert.AreEqual("T", Truthy("1"));
        [Test] public void Truthiness_NegativeNumber() => Assert.AreEqual("T", Truthy("-1"));
        [Test] public void Truthiness_Zero() => Assert.AreEqual("F", Truthy("0"));
        [Test] public void Truthiness_FractionalNumber() => Assert.AreEqual("T", Truthy("0.5"));
        [Test] public void Truthiness_Null() => Assert.AreEqual("F", Truthy("null"));
        [Test] public void Truthiness_NonEmptyString() => Assert.AreEqual("T", Truthy("\"x\""));
        [Test] public void Truthiness_EmptyString() => Assert.AreEqual("T", Truthy("\"\""));
        [Test] public void Truthiness_EmptyArray() => Assert.AreEqual("T", Truthy("{}"));

        [Test]
        public void Truthiness_ClassInstance()
        {
            var output = Run("class C end\nc = new C\nif c\nprint \"T\"\nelse\nprint \"F\"\nend");
            Assert.AreEqual("T", output[0]);
        }

        [Test] public void Not_NonZeroNumber_IsFalse() => Assert.AreEqual("False", Run("print !5")[0]);
        [Test] public void Not_Zero_IsTrue() => Assert.AreEqual("True", Run("print !0")[0]);
        [Test] public void Not_Null_IsTrue() => Assert.AreEqual("True", Run("print !null")[0]);
        [Test] public void Not_String_IsFalse() => Assert.AreEqual("False", Run("print !\"a\"")[0]);

        // ────────────────────────────────────────────────
        //  Array semantics
        // ────────────────────────────────────────────────

        [Test]
        public void ArrayRead_OutOfBounds_IsNull()
        {
            Assert.AreEqual("null", Run("arr = {1, 2}\nprint arr{99}")[0]);
        }

        [Test]
        public void ArrayWrite_OutOfBounds_IsSilentlyIgnored()
        {
            var output = Run("arr = {1, 2}\narr{99} = 5\nprint arr");
            Assert.AreEqual("[1, 2]", output[0]);
        }

        [Test]
        public void Arrays_AreReferences_MutationIsShared()
        {
            var output = Run("a = {1, 2}\nb = a\nb{0} = 99\nprint a{0}");
            Assert.AreEqual("99", output[0]);
        }

        [Test]
        public void ArrayPlusScalar_AppendsScalar()
        {
            Assert.AreEqual("[1, 2, 3]", Run("print {1, 2} + 3")[0]);
        }

        [Test]
        public void ScalarPlusArray_PrependsScalar()
        {
            Assert.AreEqual("[3, 1, 2]", Run("print 3 + {1, 2}")[0]);
        }

        [Test]
        public void ArrayPlusArray_Concatenates()
        {
            Assert.AreEqual("[1, 2]", Run("print {1} + {2}")[0]);
        }

        [Test]
        public void AppendOperator_EvaluatesToTheArray()
        {
            Assert.AreEqual("[1, 2]", Run("x = ({1} << 2)\nprint x")[0]);
        }

        [Test]
        public void AppendOperator_OnNonArray_IsSilentNoOp()
        {
            var output = Run("s = \"ab\"\ns << \"c\"\nprint s");
            Assert.AreEqual("ab", output[0]);
        }

        [Test]
        public void NestedArrays_ViaLiteral()
        {
            var output = Run("m = {{1, 2}, {3, 4}}\nrow = m{1}\nprint row{0}");
            Assert.AreEqual("3", output[0]);
        }

        // ────────────────────────────────────────────────
        //  String semantics
        // ────────────────────────────────────────────────

        [Test]
        public void StringIndexRead_OutOfBounds_IsNull()
        {
            Assert.AreEqual("null", Run("s = \"ab\"\nprint s{5}")[0]);
        }

        [Test]
        public void StringIndexWrite_MultiCharacter_InsertsWholeReplacement()
        {
            Assert.AreEqual("XYbc", Run("s = \"abc\"\ns{0} = \"XY\"\nprint s")[0]);
        }

        [Test]
        public void StringMinus_RemovesAllOccurrences()
        {
            Assert.AreEqual("bb", Run("print \"ababa\" - \"a\"")[0]);
        }

        [Test]
        public void StringRepeat_ZeroTimes_IsEmpty()
        {
            Assert.AreEqual("", Run("print \"ab\" * 0")[0]);
        }

        [Test]
        public void StringRepeat_NegativeCount_IsEmpty()
        {
            Assert.AreEqual("", Run("print \"ab\" * -2")[0]);
        }

        [Test]
        public void StringRepeat_FractionalCount_TruncatesTowardZero()
        {
            Assert.AreEqual("abab", Run("print \"ab\" * 2.9")[0]);
        }

        [Test]
        public void ConcatNumberOntoString_UsesShortestRoundTrip()
        {
            Assert.AreEqual("v=3.14", Run("print \"v=\" + 3.14")[0]);
        }

        // ────────────────────────────────────────────────
        //  Cross-type operators (documented, deterministic behavior)
        // ────────────────────────────────────────────────

        [Test]
        public void CompareStringToNumber_UsesOrdinalToString()
        {
            // Cross-type comparison falls back to ordinal string comparison:
            // "5" vs "10" compares '5' > '1'.
            Assert.AreEqual("False", Run("print \"5\" < 10")[0]);
            Assert.AreEqual("True", Run("print 10 < \"5\"")[0]);
        }

        [Test]
        public void NegateString_YieldsZero()
        {
            // Unary minus reads the numeric payload of the value, which is 0
            // for reference tags. Deterministic, if surprising.
            Assert.AreEqual("0", Run("print -\"abc\"")[0]);
        }

        [Test]
        public void NegateTrue_YieldsMinusOne()
        {
            Assert.AreEqual("-1", Run("print -true")[0]);
        }

        [Test]
        public void MultiplyStrings_IsRuntimeError_Catchable()
        {
            var output = Run("begin\nprint \"a\" * \"b\"\nrescue e\nprint \"err: \" + e\nend");
            Assert.AreEqual("err: Unable to multiply non-numeric values", output[0]);
        }

        [Test]
        public void DivideStrings_IsRuntimeError_Catchable()
        {
            var output = Run("begin\nprint \"a\" / \"b\"\nrescue e\nprint \"err: \" + e\nend");
            Assert.AreEqual("err: Unable to divide non-numeric values", output[0]);
        }

        [Test]
        public void ModuloFractional()
        {
            Assert.AreEqual("1.5", Run("print 5.5 % 2")[0]);
        }

        [Test]
        public void ModuloNegative_TruncatesTowardZero()
        {
            Assert.AreEqual("-1", Run("print -7 % 3")[0]);
        }

        // ────────────────────────────────────────────────
        //  is / as on non-class values
        // ────────────────────────────────────────────────

        [Test]
        public void Is_OnNumber_IsFalse()
        {
            Assert.AreEqual("False", Run("class Foo end\nprint 5 is Foo")[0]);
        }

        [Test]
        public void Is_OnNull_IsFalse()
        {
            Assert.AreEqual("False", Run("class Foo end\nprint null is Foo")[0]);
        }

        [Test]
        public void As_OnNumber_IsNull()
        {
            Assert.AreEqual("null", Run("class Foo end\nprint 5 as Foo")[0]);
        }

        [Test]
        public void As_UnrelatedClass_IsNull()
        {
            var output = Run("class Foo end\nclass Bar end\nb = new Bar\nprint b as Foo");
            Assert.AreEqual("null", output[0]);
        }

        // ────────────────────────────────────────────────
        //  this outside a class (regression: used to crash the VM)
        // ────────────────────────────────────────────────

        [Test]
        public void This_AtTopLevel_IsCatchableRuntimeError()
        {
            var output = Run("begin\nprint this\nrescue e\nprint \"err: \" + e\nend");
            Assert.AreEqual("err: 'this' can only be used inside a class", output[0]);
        }

        [Test]
        public void ThisProperty_AtTopLevel_IsCatchableRuntimeError()
        {
            var output = Run("begin\nprint this :: x\nrescue e\nprint \"err: \" + e\nend");
            Assert.AreEqual("err: 'this' can only be used inside a class", output[0]);
        }

        [Test]
        public void ThisPropertyWrite_AtTopLevel_IsCatchableRuntimeError()
        {
            var output = Run("begin\nthis :: x = 1\nrescue e\nprint \"err: \" + e\nend");
            Assert.AreEqual("err: 'this' can only be used inside a class", output[0]);
        }

        [Test]
        public void This_InFreeFunction_IsCatchableRuntimeError()
        {
            var output = Run(
                "fun f [] return this end\n" +
                "begin\nprint f []\nrescue e\nprint \"caught\"\nend");
            Assert.AreEqual("caught", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Runtime error messages (pinned so hosts can rely on them)
        // ────────────────────────────────────────────────

        [Test]
        public void CallUndefinedFunction_MessageNamesFunctionAndArity()
        {
            var output = Run("begin\nnope [1]\nrescue e\nprint e\nend");
            Assert.AreEqual("Function 'nope' with 1 args is not defined", output[0]);
        }

        [Test]
        public void CallWithWrongArity_IsUndefined()
        {
            var output = Run(
                "fun f [a] return a end\n" +
                "begin\nf [1, 2]\nrescue e\nprint e\nend");
            Assert.AreEqual("Function 'f' with 2 args is not defined", output[0]);
        }

        [Test]
        public void CallNonCallableValue_IsCatchable()
        {
            var output = Run("x = 5\nbegin\nx [1]\nrescue e\nprint e\nend");
            Assert.AreEqual("Function 'x' with 1 args is not defined", output[0]);
        }

        [Test]
        public void PropertyAccessOnNumber_IsCatchable()
        {
            var output = Run("begin\nprint 5 :: x\nrescue e\nprint e\nend");
            Assert.AreEqual("Cannot access property 'x' on non-class value", output[0]);
        }

        [Test]
        public void MethodCallOnNull_IsCatchable()
        {
            var output = Run("n = null\nbegin\nn :: m []\nrescue e\nprint \"caught\"\nend");
            Assert.AreEqual("caught", output[0]);
        }

        [Test]
        public void AssertFailure_ReportsLineAndStopsScript()
        {
            var output = Run("assert 1 == 2\nprint \"after\"");
            StringAssert.Contains("Assertion error at line 1", output[0]);
            Assert.IsFalse(output.Contains("after"));
        }

        [Test]
        public void AssertFailure_IsCatchable()
        {
            var output = Run("begin\nassert false\nrescue e\nprint \"caught\"\nend");
            Assert.AreEqual("caught", output[0]);
        }

        [Test]
        public void RaiseWithoutArgument_IsEmptyException()
        {
            var output = Run("begin\nraise\nrescue e\nprint \"caught: \" + e\nend");
            Assert.AreEqual("caught: Empty exception", output[0]);
        }

        [Test]
        public void RescueVariable_IsTheRaisedString()
        {
            var output = Run("begin\nraise \"boom\"\nrescue e\nprint e == \"boom\"\nend");
            Assert.AreEqual("True", output[0]);
        }

        [Test]
        public void ReturnAtTopLevel_StopsScript()
        {
            var output = Run("print 1\nreturn\nprint 2");
            Assert.AreEqual(new[] { "1" }, output);
        }

        // ────────────────────────────────────────────────
        //  VM resource limits (regressions: used to crash with
        //  IndexOutOfRangeException)
        // ────────────────────────────────────────────────

        [Test]
        public void ValueStackOverflow_IsScriptError_NotProcessCrash()
        {
            var sb = new System.Text.StringBuilder("arr = {");
            for (int i = 0; i < 1100; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i);
            }
            sb.Append("}\nprint \"built\"");

            var (script, output) = TestHelper.Run("stack_overflow", sb.ToString());
            StringAssert.Contains("Value stack overflow", output[0]);
        }

        [Test]
        public void ValueStackOverflow_IsCatchable()
        {
            var sb = new System.Text.StringBuilder("begin\narr = {");
            for (int i = 0; i < 1100; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i);
            }
            sb.Append("}\nrescue e\nprint \"caught: \" + e\nend");

            var output = Run(sb.ToString());
            Assert.AreEqual("caught: Value stack overflow", output[0]);
        }

        [Test]
        public void HandlerStackOverflow_IsScriptError_NotProcessCrash()
        {
            // 33 nested begins exceed the 32-slot handler stack; the overflow
            // itself is raised as a script exception and caught by the
            // already-registered outer handlers.
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 33; i++) sb.Append("begin\n");
            sb.Append("print \"unreachable\"\n");
            for (int i = 0; i < 33; i++) sb.Append("rescue e\nend\n");
            sb.Append("print \"survived\"");

            var output = Run(sb.ToString());
            Assert.IsFalse(output.Contains("unreachable"));
            Assert.AreEqual("survived", output[output.Count - 1]);
        }

        [Test]
        public void CallFrameOverflow_IsCatchable()
        {
            var output = Run(
                "fun infinite [n] return 1 + infinite [n + 1] end\n" +
                "begin\ninfinite [0]\nrescue e\nprint e\nend");
            Assert.AreEqual("Stack overflow", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Named arguments — edge behavior (documented)
        // ────────────────────────────────────────────────

        [Test]
        public void NamedArgs_UnknownName_IsDroppedAndMissingParamIsNull()
        {
            // Unknown names are silently discarded; parameters without a
            // matching named argument arrive as null.
            var output = Run(
                "fun mk [a, b] return \"\" + a + b end\n" +
                "print mk [a: 1, zzz: 2]");
            Assert.AreEqual("1null", output[0]);
        }

        [Test, Timeout(10000)]
        public void NamedArgs_OnClassConstructor_AreNotSupported_SyntaxError()
        {
            // Constructor argument lists only accept positional expressions.
            // This used to hang the parser; now it must be a clean error.
            Assert.Throws<WarScript.Exception.SyntaxException>(() => TestHelper.Run(
                "named_ctor",
                "class P [x, y] end\np = new P [y: 2, x: 1]"));
        }

        // ────────────────────────────────────────────────
        //  Lambdas — identity, printing, recursion
        // ────────────────────────────────────────────────

        [Test]
        public void Lambda_EqualityIsReferenceIdentity()
        {
            Assert.AreEqual("True", Run("f = fun [x] return x end\ng = f\nprint f == g")[0]);
        }

        [Test]
        public void TwoLambdas_AreNotEqual()
        {
            var output = Run(
                "f = fun [x] return x end\n" +
                "g = fun [x] return x end\n" +
                "print f == g");
            Assert.AreEqual("False", output[0]);
        }

        [Test]
        public void Lambda_RecursionThroughGlobalName()
        {
            var output = Run(
                "f = fun [n] if n <= 0 return 0 end return f [n - 1] end\n" +
                "print f [3]");
            Assert.AreEqual("0", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Enum edges
        // ────────────────────────────────────────────────

        [Test]
        public void Enum_NegativeExplicitLiteral()
        {
            var output = Run("enum E\nA = -1\nB\nend\nprint E :: A\nprint E :: B");
            Assert.AreEqual(new[] { "-1", "0" }, output);
        }

        [Test]
        public void Enum_Empty_HasZeroCount()
        {
            Assert.AreEqual("0", Run("enum E\nend\nprint E :: count")[0]);
        }

        [Test]
        public void Enum_MemberInArithmetic()
        {
            var output = Run("enum E\nA = 3\nend\nprint E :: A * 2 + 1");
            Assert.AreEqual("7", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Interpolation of composite values (documented behavior)
        // ────────────────────────────────────────────────

        [Test]
        public void Interpolation_OfClassInstance_UsesClassName()
        {
            var output = Run("class P [x] end\np = new P [1]\nprint \"p: {p}\"");
            Assert.AreEqual("p: P", output[0]);
        }

        [Test]
        public void Interpolation_OfArray_ProducesArrayViaPlusDesugar()
        {
            // Interpolation desugars to `+`, and string + array follows the
            // array-concat rule — the result is an array, not text. Pinned as
            // the current (surprising but deterministic) behavior.
            var output = Run("a = {1, 2}\nprint \"arr: {a}\"");
            Assert.AreEqual("[arr: , 1, 2]", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Comments
        // ────────────────────────────────────────────────

        [Test]
        public void EmptySource_RunsWithoutError()
        {
            Assert.IsEmpty(Run(""));
        }

        [Test]
        public void CommentOnlySource_RunsWithoutError()
        {
            Assert.IsEmpty(Run("# just a comment"));
        }

        [Test]
        public void HashInsideString_IsNotAComment()
        {
            Assert.AreEqual("a#b", Run("print \"a#b\"")[0]);
        }

        [Test]
        public void CommentAfterStatement_OnSameLine()
        {
            Assert.AreEqual("1", Run("print 1 # trailing comment")[0]);
        }
    }
}
