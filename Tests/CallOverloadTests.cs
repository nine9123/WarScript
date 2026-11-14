using System;
using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Allocation-free call overloads (v2 plan T2.7, API-4):
    /// <c>Call(FunctionHandle, ReadOnlySpan&lt;WarValue&gt;)</c> and
    /// <c>StartCoroutine(name, ReadOnlySpan&lt;WarValue&gt;, loop, out id)</c>
    /// next to the <c>params</c>/array overloads.
    /// </summary>
    [TestFixture]
    public class CallOverloadTests
    {
        private static (WarScriptLanguage script, List<string> output) Script(string source)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("main", source, null, (s, m) => output.Add(m));
            script.LogUnhandledErrors = false;
            return (script, output);
        }

        private static WarValue N(int n) => WarValue.FromNumeric(F64.FromInt(n));

        [Test]
        public void SpanCall_PassesTheSpansElements()
        {
            var (script, _) = Script("fun sub [a, b]\n    return a - b\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            var sub = script.GetFunction("sub", 2);

            var buffer = new[] { N(99), N(10), N(3), N(99) };
            var result = script.Call(sub, new ReadOnlySpan<WarValue>(buffer, 1, 2));

            Assert.AreEqual(ScriptStatus.Ok, result.Status, result.ToString());
            Assert.IsTrue(result.Value.Numeric == 7);
        }

        [Test]
        public void SpanCall_ShorterThanTheArity_PadsWithNull_SoDefaultsApply()
        {
            var (script, _) = Script("fun f [a, b = 5]\n    return a + b\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            // The handle of the full arity, called with one argument: the
            // missing one is null, which triggers the default.
            var f = script.GetFunction("f", 2);

            var result = script.Call(f, new ReadOnlySpan<WarValue>(new[] { N(1) }));

            Assert.IsTrue(result.Value.Numeric == 6, result.ToString());
        }

        [Test]
        public void SpanCall_EmptySpan_CallsAZeroArityFunction()
        {
            var (script, _) = Script("fun f []\n    return 42\nend\n");
            Assert.IsTrue(script.Run().IsOk);

            var result = script.Call(script.GetFunction("f", 0), ReadOnlySpan<WarValue>.Empty);

            Assert.IsTrue(result.Value.Numeric == 42, result.ToString());
        }

        [Test]
        public void ParamsCall_WithANullArray_IsAnEmptyArgumentList()
        {
            var (script, _) = Script("fun f [a]\n    return a == null\nend\n");
            Assert.IsTrue(script.Run().IsOk);

            var result = script.Call(script.GetFunction("f", 1), (WarValue[])null);

            Assert.AreEqual(ScriptStatus.Ok, result.Status, result.ToString());
            Assert.IsTrue(result.Value.LogicalValue);
        }

        [Test]
        public void SpanCall_ReportsErrorsAndRefusalsLikeTheArrayOverload()
        {
            var (script, _) = Script("fun f [a]\n    return a / 0\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            var f = script.GetFunction("f", 1);

            var result = script.Call(f, new ReadOnlySpan<WarValue>(new[] { N(1) }));
            Assert.AreEqual(ScriptStatus.Error, result.Status);
            Assert.AreEqual(ScriptErrorKind.DivisionByZero, result.Error.Kind);
            Assert.IsTrue(script.IsHalted);

            Assert.AreEqual(ScriptStatus.Halted, script.Call(f, new ReadOnlySpan<WarValue>(new[] { N(1) })).Status);
        }

        [Test]
        public void SpanStartCoroutine_CopiesTheArguments_SoTheBufferCanBeReused()
        {
            var (script, output) = Script(
                "fun co [a]\n    print a\n    yield\n    print a\nend\n");
            Assert.IsTrue(script.Run().IsOk);

            var buffer = new[] { N(1) };
            var result = script.StartCoroutine("co", new ReadOnlySpan<WarValue>(buffer), true, out var id);
            Assert.AreEqual(ScriptStatus.Ok, result.Status, result.ToString());
            Assert.GreaterOrEqual(id, 0);

            buffer[0] = N(2);
            script.TickCoroutines(F64.Zero); // second half of the first run
            script.TickCoroutines(F64.Zero); // the looping restart

            CollectionAssert.AreEqual(new[] { "1", "1", "1" }, output);
        }

        [Test]
        public void SpanStartCoroutine_LooksTheFunctionUpByTheSpansLength()
        {
            var (script, _) = Script("fun co [a]\n    yield\nend\n");
            Assert.IsTrue(script.Run().IsOk);

            var result = script.StartCoroutine("co", ReadOnlySpan<WarValue>.Empty, false, out var id);

            Assert.AreEqual(-1, id);
            Assert.AreEqual(ScriptErrorKind.NameError, result.Error.Kind);
        }

        [Test]
        public void TickCoroutines_ValueIsTheNumberOfActiveCoroutines()
        {
            var (script, _) = Script("fun co [n]\n    loop i in 0..n\n        yield\n    end\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            script.StartCoroutine("co", new ReadOnlySpan<WarValue>(new[] { N(1) }), false, out _);
            script.StartCoroutine("co", new ReadOnlySpan<WarValue>(new[] { N(3) }), false, out _);

            Assert.IsTrue(script.TickCoroutines(F64.Zero).Value.Numeric == 1);
            Assert.IsTrue(script.TickCoroutines(F64.Zero).Value.Numeric == 1);
            Assert.IsTrue(script.TickCoroutines(F64.Zero).Value.Numeric == 0);
        }

        [Test]
        [Category("HeadlessOnly")]
        public void SpanCall_FromAReusedBuffer_DoesNotAllocate()
        {
            if (Tests.Golden.IsolatedRun.RunnerPath() == null)
                Assert.Ignore("Allocation counting is measured in the headless build only (plan T2.7).");

            var (script, _) = Script("total = 0\nfun tick [dt, k]\n    total += dt * k\n    return total\nend\n");
            Assert.IsTrue(script.Run().IsOk);
            var tick = script.GetFunction("tick", 2);
            var buffer = new WarValue[4];
            buffer[0] = N(1);
            buffer[1] = N(2);
            for (var i = 0; i < 10; i++) script.Call(tick, new ReadOnlySpan<WarValue>(buffer, 0, 2)); // warm up

            const int count = 1000;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++) script.Call(tick, new ReadOnlySpan<WarValue>(buffer, 0, 2));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestContext.Progress.WriteLine("T2.7: " + allocated / count + " bytes allocated per Call");
            Assert.AreEqual(0, allocated, "bytes allocated by " + count + " calls");
            Assert.IsTrue(script.Call(tick, new ReadOnlySpan<WarValue>(buffer, 0, 2)).Value.Numeric == 2 * (count + 11));
        }
    }
}
