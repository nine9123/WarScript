using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Pins remaining semantic corners: lambda recursion and (non-)capture
    /// rules, named arguments with defaults and on native functions, const/enum
    /// placement, chained comparisons, `new` in parenthesized expressions,
    /// negative/zero loop steps, raise inside ensure/rescue, the import-const
    /// protection gap, sibling method calls, fresh-instance determinism, and
    /// breakpoints inside lambda bodies.
    /// </summary>
    [TestFixture]
    public class RuntimeSemanticsEdgeTests
    {
        private static List<string> Run(string source)
        {
            var (_, output) = TestHelper.Run("semantics_edge", source);
            return output;
        }

        // ────────────────────────────────────────────────
        //  Lambda recursion & capture
        // ────────────────────────────────────────────────

        [Test]
        public void GlobalLambda_DeepSelfRecursionThroughGlobalName_IsTailCallOptimized()
        {
            // Recursion through a *global* lambda name goes through the Call
            // path and gets TCO — 10000 frames deep must not overflow the
            // 128-slot frame stack.
            var output = Run(@"
count = fun [n]
    if n == 0
        return 0
    end
    return count [n - 1]
end
begin
    print ""ok "" + count [10000]
rescue err
    print ""caught: "" + err
end
");
            Assert.AreEqual("ok 0", output[0]);
        }

        [Test]
        public void LocalLambda_CannotSelfRecurse_NameNotVisibleInsideBody()
        {
            // Lambdas have no closures: inside the lambda body, a function-local
            // variable holding the lambda itself is not visible, so the
            // self-call resolves to an undefined function (catchable).
            var output = Run(@"
fun run_deep []
    step = fun [n]
        if n == 0
            return ""done""
        end
        return step [n - 1]
    end
    return step [5000]
end
begin
    print run_deep []
rescue err
    print ""caught: "" + err
end
");
            Assert.AreEqual("caught: Function 'step' with 1 args is not defined", output[0]);
        }

        [Test]
        public void Lambda_DoesNotCaptureOuterLocal_ReadsAsNull()
        {
            var output = Run(@"
fun make []
    local = 42
    return fun [] return local end
end
f = make []
print f []
");
            Assert.AreEqual("null", output[0]);
        }

        [Test]
        public void GlobalLambda_CallableFromInsideNamedFunction()
        {
            var output = Run(@"
g = fun [x] return x * 3 end
fun use []
    return g [7]
end
print use []
");
            Assert.AreEqual("21", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Named arguments
        // ────────────────────────────────────────────────

        [Test]
        public void NamedArgs_OmittedDefaultParameter_GetsDefault()
        {
            var output = Run(@"
fun greet [name, greeting = ""Hello""]
    return greeting + "", "" + name
end
print greet [name: ""Bob""]
");
            Assert.AreEqual("Hello, Bob", output[0]);
        }

        [Test]
        public void NamedArgs_Reordered_WithDefaultParameter()
        {
            var output = Run(@"
fun greet [name, greeting = ""Hello""]
    return greeting + "", "" + name
end
print greet [greeting: ""Yo"", name: ""Ann""]
");
            Assert.AreEqual("Yo, Ann", output[0]);
        }

        [Test]
        public void NamedArgs_OnNativeFunction_MapByDeclaredParameterName()
        {
            // clamp is declared [n, lo, hi]. Passing the names out of order
            // proves mapping is by name: clamp[n:99, lo:0, hi:10] == 10,
            // whereas positional (10, 99, 0) would give 0.
            var (_, output) = TestHelper.Run("named_native",
                "print clamp [hi: 10, n: 99, lo: 0]",
                (s, scope) => MathLibrary.Register(s, scope));

            Assert.AreEqual("10", output[0]);
        }

        // ────────────────────────────────────────────────
        //  const / enum placement
        // ────────────────────────────────────────────────

        [Test]
        public void ConstInsideFunctionBody_IsAllowed()
        {
            var output = Run(@"
fun f []
    const LOCAL = 9
    return LOCAL
end
print f []
");
            Assert.AreEqual("9", output[0]);
        }

        [Test]
        public void ConstWithExpressionInitializer_Evaluates()
        {
            Assert.AreEqual("5", Run("const X = 2 + 3\nprint X")[0]);
        }

        [Test]
        public void EnumInsideFunctionBody_IsNotSupported_RuntimeError()
        {
            // The enum desugars to a class definition + singleton instantiation;
            // inside a function body the class never gets registered, so the
            // singleton assignment fails at call time.
            var output = Run(@"
fun f []
    enum E
        A
    end
    return E :: A
end
print f []
");
            Assert.IsTrue(output.Count > 0 && output[0].Contains("Class 'E' is not defined"));
        }

        [Test]
        public void ImportedConst_IsNotProtectedInImportingScript()
        {
            // Const protection is parse-time, but the importing script is fully
            // parsed before the import executes — so a const defined in an
            // imported file can be reassigned by the importer. Pinned as a
            // documented limitation.
            var (_, output) = TestHelper.Run("import_const", @"
import ""lib""
LIMIT = 3
print LIMIT
", fileResolver: name => name == "lib" ? "const LIMIT = 7\n" : null);

            Assert.AreEqual("3", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Expression corners
        // ────────────────────────────────────────────────

        [Test]
        public void ChainedComparison_EvaluatesLeftToRight_NotMathematically()
        {
            // (1 < 2) → true, then true < 3 falls back to ToString comparison
            // ("True" < "3" is false). There is no Python-style chaining.
            Assert.AreEqual("False", Run("print 1 < 2 < 3")[0]);
        }

        [Test]
        public void NewInParenthesizedExpression_AllowsImmediatePropertyAccess()
        {
            var output = Run(@"
class P [x, y]
end
print (new P [3, 4]) :: x
");
            Assert.AreEqual("3", output[0]);
        }

        [Test]
        public void SiblingMethod_CallableWithoutThisPrefix()
        {
            var output = Run(@"
class C []
    fun helper [] return 5 end
    fun caller [] return helper [] end
    fun caller2 [] return this :: helper [] end
end
c = new C []
print c :: caller []
print c :: caller2 []
");
            Assert.AreEqual("5", output[0]);
            Assert.AreEqual("5", output[1]);
        }

        // ────────────────────────────────────────────────
        //  Loop step edge cases
        // ────────────────────────────────────────────────

        [Test]
        public void DescendingRangeWithNegativeStep_RunsZeroTimes()
        {
            // Ranges only iterate upward; a negative step does not enable
            // descending iteration — the loop body never runs.
            var output = Run(@"
loop i in 10..0 by -2
    print i
end
print ""end""
");
            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("end", output[0]);
        }

        [Test]
        public void ZeroStep_InfiniteLoop_IsStoppedByInstructionBudget()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("zero_step", @"
begin
    loop i in 0..10 by 0
        x = 1
    end
    print ""finished""
rescue err
    print ""caught: "" + err
end
", null, (s, m) => output.Add(m));
            script.InstructionBudget = 100000;
            script.Run();

            Assert.AreEqual("caught: Instruction budget exceeded", output[0]);
        }

        // ────────────────────────────────────────────────
        //  raise inside ensure / rescue
        // ────────────────────────────────────────────────

        [Test]
        public void RaiseInsideEnsure_PropagatesToOuterHandler()
        {
            var output = Run(@"
begin
    begin
        print ""body""
    ensure
        raise ""from ensure""
    end
rescue err
    print ""outer caught: "" + err
end
");
            Assert.AreEqual("body", output[0]);
            Assert.AreEqual("outer caught: from ensure", output[1]);
        }

        [Test]
        public void ReraiseFromRescue_PropagatesToOuterHandler()
        {
            var output = Run(@"
begin
    begin
        raise ""first""
    rescue err
        raise ""second: "" + err
    end
rescue err2
    print ""outer caught: "" + err2
end
");
            Assert.AreEqual("outer caught: second: first", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Determinism across fresh instances
        // ────────────────────────────────────────────────

        [Test]
        public void FreshInstances_ProduceIdenticalOutput()
        {
            const string src = @"
total = 0
loop i in 0..100
    total += i * 3 - i / 7
end
print total
print sqrt [2]
print pow [3, 0.5]
print sin [1]
";
            var (_, o1) = TestHelper.Run("det_a", src, (s, scope) => MathLibrary.Register(s, scope));
            var (_, o2) = TestHelper.Run("det_b", src, (s, scope) => MathLibrary.Register(s, scope));

            CollectionAssert.AreEqual(o1, o2);
        }

        // ────────────────────────────────────────────────
        //  Debugger inside lambda bodies
        // ────────────────────────────────────────────────

        [Test]
        public void BreakpointFiresInsideLambdaBody()
        {
            var hitLines = new List<int>();
            var (_, output) = TestHelper.Run("lambda_debug", @"
f = fun [x]
    y = x + 1
    return y
end
print f [1]
", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3); // y = x + 1
                s.DebugHook = ctx => { hitLines.Add(ctx.Line); ctx.Action = StepMode.Continue; };
            });

            Assert.AreEqual(new[] { 3 }, hitLines);
            Assert.AreEqual("2", output[0]);
        }
    }
}
