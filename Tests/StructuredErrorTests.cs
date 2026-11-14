using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    /// <summary>
    /// Structured errors (v2 plan T2.5): every error the runtime raises has
    /// a kind, the line it was raised at and a call stack, innermost frame
    /// first. The kind of each error family and its line are also covered by
    /// the goldens in <c>Tests/Golden~/errors/</c>.
    /// </summary>
    [TestFixture]
    public class StructuredErrorTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source,
            Func<string, string> files = null)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source, files, (s, m) => output.Add(m));
            script.LogUnhandledErrors = false;
            WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
            return (script, output);
        }

        private static void AddNative(WarScriptLanguage script, string name, int arity, Func<List<WarValue>, WarValue> body)
        {
            var parameters = new List<string>();
            for (var i = 0; i < arity; i++) parameters.Add("a" + i);
            script.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, parameters), body, "", ""));
        }

        private static string Frames(ScriptError error) =>
            string.Join(" < ", error.CallStack.Select(f => f.Function + "@" + f.ScriptName + ":" + f.Line));

        [Test]
        public void CallStack_IsInnermostFirst_WithTheLineEachFrameIsAt()
        {
            var (script, _) = Script(@"fun inner [x]
    return x / 0
end
fun outer []
    y = 1
    return inner [y] + 1
end
outer []
");
            var result = script.Run();

            Assert.AreEqual(ScriptStatus.Error, result.Status);
            var error = result.Error;
            Assert.AreEqual(ScriptErrorKind.DivisionByZero, error.Kind);
            Assert.AreEqual("main", error.ScriptName);
            Assert.AreEqual(2, error.Line);
            Assert.AreEqual(0, error.Column);
            Assert.AreEqual("inner@main:2 < outer@main:6 < <script>@main:8", Frames(error));
            Assert.AreEqual("main:2:0: DivisionByZero: Division by zero\n"
                            + "  at inner (main:2:0)\n"
                            + "  at outer (main:6:0)\n"
                            + "  at <script> (main:8:0)", error.ToString());
        }

        [Test]
        public void CallStack_NamesLambdasConstructorsAndMethods()
        {
            var (script, _) = Script(@"class Unit [hp]
    fun hit [f]
        return f [this :: hp]
    end
end
class Spawner []
    u = new Unit [0]
    r = u :: hit [fun [x] return 1 / x end]
end
s = new Spawner []
");
            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            // Methods carry their own name until classes get qualified names.
            Assert.AreEqual("<lambda>@main:8 < hit@main:3 < Spawner#ctor@main:8 < <script>@main:10", Frames(result.Error));
        }

        [Test]
        public void ErrorInAnImportedFile_NamesThatFile()
        {
            var (script, _) = Script("import \"lib\"\nprint \"not reached\"\n",
                path => path == "lib" ? "x = 1\ny = x / 0\n" : null);

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            Assert.AreEqual("lib", result.Error.ScriptName);
            Assert.AreEqual(2, result.Error.Line);
            Assert.AreEqual("<script>@lib:2", result.Error.CallStack[0].Function + "@" + result.Error.CallStack[0].ScriptName + ":" + result.Error.CallStack[0].Line);
            Assert.AreEqual("main", result.Error.CallStack[result.Error.CallStack.Count - 1].ScriptName);
        }

        [Test]
        public void CallbackError_StackContinuesIntoTheCallingScript()
        {
            // A nested entry's error continues from the native call site
            // (PROP-1): the stack holds the callback's frames, then the frames
            // of the script that called the native.
            WarScriptLanguage s = null;
            var (script, _) = Script(@"fun on_event []
    return missing []
end
fun main []
    fire []
end
main []
");
            s = script;
            AddNative(script, "fire", 0, _ =>
            {
                s.Call(s.GetFunction("on_event", 0));
                return WarValue.Null;
            });

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.NameError, result.Error.Kind);
            Assert.AreEqual(2, result.Error.Line);
            Assert.AreEqual("on_event@main:2 < main@main:5 < <script>@main:7", Frames(result.Error));
        }

        [Test]
        public void ErrorReRaisedByAnEnsureBlock_KeepsItsKindAndStack()
        {
            var (script, output) = Script(@"fun f []
    begin
        x = 1 / 0
    ensure
        print ""ensure""
    end
end
f []
");
            var result = script.Run();

            CollectionAssert.AreEqual(new[] { "ensure" }, output);
            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            Assert.AreEqual("f@main:3 < <script>@main:8", Frames(result.Error));
        }

        [Test]
        public void InstructionBudgetExceeded_InsideBegin_KeepsWhereItRanOut()
        {
            // BUDGET-2: every instruction after the budget ran out raises
            // again, here at the rescue handler of the script body; the error
            // the host sees is still the one raised inside spin.
            var (script, output) = Script(@"fun spin []
    loop true
        x = 1
    end
end
begin
    spin []
rescue e
    print ""rescued""
end
");
            script.InstructionBudget = 1000;
            var result = script.Run();

            CollectionAssert.IsEmpty(output);
            Assert.AreEqual(ScriptErrorKind.InstructionBudgetExceeded, result.Error.Kind);
            Assert.AreEqual("spin", result.Error.CallStack[0].Function);
            Assert.That(result.Error.Line, Is.InRange(2, 3));
            Assert.AreEqual(2, result.Error.CallStack.Count);
            Assert.AreEqual("<script>@main:7", Frames(result.Error).Split(new[] { " < " }, StringSplitOptions.None)[1]);
        }

        [Test]
        public void ErrorReRaisedFromARescueBody_IsANewRaisedError()
        {
            // `raise e` raises the message text again: a Raised error at the
            // raise (PROP-22: e is the text, not an error object).
            var (script, _) = Script(@"begin
    x = 1 / 0
rescue e
    raise e
end
");
            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.Raised, result.Error.Kind);
            Assert.AreEqual("Division by zero", result.Error.Message);
            Assert.AreEqual(4, result.Error.Line);
        }

        [Test]
        public void RaisedNonTextValue_KeepsTheValue()
        {
            var (script, _) = Script("raise {1, 2}\n");

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.Raised, result.Error.Kind);
            Assert.IsTrue(result.Error.Value.IsArray);
            Assert.AreEqual(1, result.Error.Line);
        }

        // ── Natives (plan 6.9) ──────────────────────────────────

        [Test]
        public void NativeHelper_MissingArgument_IsAnArgumentError()
        {
            var (script, _) = Script("x = needs_two [1]\n");
            AddNative(script, "needs_two", 1, args =>
                WarValue.FromNumeric(NativeHelper.NumericArg(args, 0) + NativeHelper.NumericArg(args, 1)));

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.ArgumentError, result.Error.Kind);
            Assert.AreEqual("Native function 'needs_two' failed: Expected argument at position 1, but only 1 were provided",
                result.Error.Message);
            Assert.AreEqual(1, result.Error.Line);
        }

        [Test]
        public void NativeHelper_WrongType_IsATypeError_AndRescuable()
        {
            var (script, output) = Script(@"begin
    x = takes_text [5]
rescue e
    print e
end
");
            AddNative(script, "takes_text", 1, args => WarValue.FromText(NativeHelper.TextArg(args, 0)));

            var result = script.Run();

            Assert.IsTrue(result.IsOk, result.ToString());
            CollectionAssert.AreEqual(new[] { "Native function 'takes_text' failed: Argument 0 expected Text, got Numeric" }, output);
        }

        [Test]
        public void NativeHelper_ThrowsNativeArgumentException_OutsideTheVM()
        {
            var args = new List<WarValue> { WarValue.FromNumeric(FixMath.F64.One) };
            var e = Assert.Throws<NativeArgumentException>(() => NativeHelper.TextArg(args, 0));
            Assert.AreEqual(ScriptErrorKind.TypeError, e.Kind);
            e = Assert.Throws<NativeArgumentException>(() => NativeHelper.TextArg(args, 1));
            Assert.AreEqual(ScriptErrorKind.ArgumentError, e.Kind);
        }

        [Test]
        public void NativeThrowingScriptErrorException_HasThatKind_AtTheCallLine()
        {
            var (script, _) = Script("x = 1\ny = lookup [x]\n");
            AddNative(script, "lookup", 1, _ => throw new ScriptErrorException(ScriptErrorKind.IndexError, "no such slot"));

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.IndexError, result.Error.Kind);
            Assert.AreEqual("no such slot", result.Error.Message);
            Assert.AreEqual(2, result.Error.Line);
        }

        [Test]
        public void NativeThrowingAnythingElse_IsANativeError()
        {
            var (script, _) = Script("x = boom []\n");
            AddNative(script, "boom", 0, _ => throw new InvalidOperationException("bad state"));

            var result = script.Run();

            Assert.AreEqual(ScriptErrorKind.NativeError, result.Error.Kind);
            Assert.AreEqual("Native function 'boom' failed: bad state", result.Error.Message);
            Assert.AreEqual(1, result.Error.Line);
        }

        // ── Entries without frames, coroutines ──────────────────

        [Test]
        public void RefusedHostEntry_HasNoFramesAndLineZero()
        {
            var (script, _) = Script("x = 1\n");
            script.Run();

            var result = script.StartCoroutine("missing", null, false, out _);

            Assert.AreEqual(ScriptErrorKind.NameError, result.Error.Kind);
            Assert.AreEqual(0, result.Error.Line);
            Assert.AreEqual(0, result.Error.CallStack.Count);
        }

        [Test]
        public void CoroutineError_HasTheCoroutinesFrames()
        {
            var (script, _) = Script(@"fun co []
    yield
    assert false
end
");
            script.Run();
            Assert.IsTrue(script.StartCoroutine("co", null, false, out _).IsOk);

            var tick = script.TickCoroutines(FixMath.F64.One);

            Assert.AreEqual(ScriptErrorKind.AssertionFailed, tick.Error.Kind);
            Assert.AreEqual(3, tick.Error.Line);
            Assert.AreEqual("co@main:3", Frames(tick.Error));
        }

        [Test]
        public void HostFault_HasNoCallStack()
        {
            var (script, _) = Script("fun f []\n    x = 1\nend\n");
            script.Run();
            script.AddBreakpoint(2);
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");

            var result = script.Call(script.GetFunction("f", 0));

            Assert.AreEqual(ScriptErrorKind.HostFault, result.Error.Kind);
            Assert.AreEqual(0, result.Error.CallStack.Count);
        }
    }
}
