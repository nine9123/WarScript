using System;
using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// The VM's exception boundary (plan T1.3): exceptions thrown by natives
    /// become rescuable script errors, an unhandled error unwinds every frame,
    /// and any other C# exception inside the VM is a host fault that restores
    /// the context stacks and stops the entry instead of escaping to the host.
    /// </summary>
    [TestFixture]
    public class ExceptionBoundaryTests
    {
        private static void AddNative(DefinitionScope scope, string name, Func<List<WarValue>, WarValue> body)
        {
            scope.AddFunction(new NativeFunctionDefinition(
                new FunctionDetails(name, new List<string>()), body, "", ""));
        }

        // ── Natives ─────────────────────────────────────────────────────

        [Test]
        public void NativeThrowingAnyException_IsRescuedAsNativeError()
        {
            var (_, output) = TestHelper.Run("t", @"
begin
    boom []
    print ""not reached""
rescue e
    print e
end
print ""done""", (s, scope) => AddNative(scope, "boom", _ => throw new InvalidOperationException("went wrong")));

            CollectionAssert.AreEqual(new[] { "Native function 'boom' failed: went wrong", "done" }, output);
        }

        [Test]
        public void NativeThrowingScriptErrorException_RaisesItsOwnMessage()
        {
            var (_, output) = TestHelper.Run("t", @"
begin
    check []
rescue e
    print e
end", (s, scope) => AddNative(scope, "check",
                _ => throw new ScriptErrorException(ScriptErrorKind.ArgumentError, "check needs a unit")));

            CollectionAssert.AreEqual(new[] { "check needs a unit" }, output);
        }

        [Test]
        public void NativeTailCallThrowing_IsRescuedByTheCaller()
        {
            // 'return boom []' is compiled as a tail call into the native.
            var (_, output) = TestHelper.Run("t", @"
fun f []
    return boom []
end
begin
    f []
rescue e
    print e
end", (s, scope) => AddNative(scope, "boom", _ => throw new InvalidOperationException("tail")));

            CollectionAssert.AreEqual(new[] { "Native function 'boom' failed: tail" }, output);
        }

        // ── Unhandled errors ────────────────────────────────────────────

        [Test]
        public void UnhandledErrorDeepInCalls_UnwindsEveryFrame()
        {
            // TestHelper.Run checks the invariants: every scope and class
            // context pushed by the method, the function and the block is gone.
            var (script, output) = TestHelper.Run("t", @"
class Box [v]
    fun div [d]
        if true
            return this :: v / d
        end
    end
end
fun outer []
    b = new Box [1]
    loop i in 0..3
        b :: div [0]
    end
end
outer []
print ""not reached""");

            Assert.IsFalse(output.Contains("not reached"));
            Assert.IsNotNull(script.ExceptionContext.LastUnhandled);
            StringAssert.StartsWith("Division by zero", script.ExceptionContext.LastUnhandled.Value.ToString());
        }

        // ── Host faults ─────────────────────────────────────────────────

        private const string HostFaultSource = @"class Box [v]
    w = v + 1
    fun get []
        return this :: w
    end
end
fun inner []
    begin
        b = new Box [1]
        return b :: get []
    rescue e
        print ""rescued a host fault""
    end
end
print ""before""
print inner []
print ""not reached""";

        // Line 2 runs in the constructor (a nested VM in v1), line 4 in a method.
        [TestCase(2)]
        [TestCase(4)]
        public void HostFault_StopsTheEntryAndRestoresTheContexts(int faultLine)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("t", HostFaultSource, null, (s, m) => output.Add(m));
            script.AddBreakpoint(faultLine);
            script.DebugHook = ctx =>
            {
                if (ctx.Line == faultLine) throw new InvalidOperationException("host failed");
                ctx.Action = StepMode.Continue;
            };

            var before = Invariants.Capture(script);
            Assert.DoesNotThrow(() => script.Run());

            Assert.IsNull(Invariants.Violation(script, before));
            Assert.IsInstanceOf<InvalidOperationException>(script.LastHostFault);
            Assert.AreEqual(2, output.Count, string.Join(" | ", output));
            Assert.AreEqual("before", output[0]);
            StringAssert.StartsWith("[WarScript] Host fault in 't': System.InvalidOperationException: host failed", output[1]);

            // The instance is usable again.
            script.DebugHook = null;
            output.Clear();
            script.Call(script.GetFunction("inner", 0));
            Assert.IsNull(Invariants.Violation(script, before));
            CollectionAssert.IsEmpty(output);
        }

        [Test]
        public void HostFault_InCoroutine_EndsTheCoroutine()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("t", @"fun co []
    loop true
        yield
        print ""tick""
    end
end", null, (s, m) => output.Add(m));
            script.Run();
            script.StartCoroutine("co", null);
            script.TickCoroutines(F64.FromInt(1));
            CollectionAssert.AreEqual(new[] { "tick" }, output);

            script.AddBreakpoint(4);
            script.DebugHook = ctx => throw new InvalidOperationException("host failed");
            var before = Invariants.Capture(script);

            Assert.AreEqual(0, script.TickCoroutines(F64.FromInt(1)));
            Assert.IsNull(Invariants.Violation(script, before));
            Assert.IsInstanceOf<InvalidOperationException>(script.LastHostFault);
        }

        [Test]
        public void SyntaxErrorInImport_StillReachesTheHost_WithContextsRestored()
        {
            var script = new WarScriptLanguage("t", @"fun f []
    import ""broken""
end
f []", path => "fun g [\nreturn 1\nend", null);
            var before = Invariants.Capture(script);

            Assert.Throws<SyntaxException>(() => script.Run());
            Assert.IsNull(Invariants.Violation(script, before));
        }
    }
}
