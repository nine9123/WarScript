using System.Collections.Generic;
using FixMath;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;

namespace Tests
{
    /// <summary>
    /// Pins host-facing coroutine semantics that were previously untested:
    /// exception and budget interaction, ensure/rescue across yields, edge-case
    /// dt values, StopCoroutine/StartCoroutine failure modes, what happens when
    /// a plain Call() hits a yield, and breakpoints inside coroutine bodies.
    /// </summary>
    [TestFixture]
    public class CoroutineHostApiTests
    {
        // ────────────────────────────────────────────────
        //  Exceptions inside coroutines
        // ────────────────────────────────────────────────

        [Test]
        public void UncaughtRaiseInsideCoroutine_KillsCoroutine_LogsError_LeavesNoRaisedState()
        {
            var (script, output) = TestHelper.Run("co_raise", @"
fun co []
    yield
    raise ""boom""
end
");
            script.StartCoroutine("co", null);
            Assert.AreEqual(1, script.ActiveCoroutineCount);

            script.TickCoroutines(F64.FromInt(1));

            Assert.AreEqual(0, script.ActiveCoroutineCount, "failed coroutine should be removed");
            Assert.IsFalse(script.ExceptionContext.IsRaised(), "exception must not leak into host state");
            Assert.IsTrue(output.Count > 0 && output[0].Contains("boom"), "error should reach the logger");
        }

        [Test]
        public void RescueSpansYield_ExceptionAfterResumeIsStillCaught()
        {
            var (script, output) = TestHelper.Run("co_rescue", @"
fun co []
    begin
        print ""before""
        yield
        raise ""late""
    rescue err
        print ""rescued: "" + err
    end
end
");
            script.StartCoroutine("co", null);
            script.TickCoroutines(F64.FromInt(1));

            Assert.AreEqual(0, script.ActiveCoroutineCount);
            Assert.AreEqual("before", output[0]);
            Assert.AreEqual("rescued: late", output[1]);
        }

        [Test]
        public void InstructionBudgetExceededInsideCoroutine_KillsCoroutine()
        {
            var (script, output) = TestHelper.Run("co_budget", @"
fun co []
    yield
    loop true
        x = 1
    end
end
");
            script.StartCoroutine("co", null);
            script.InstructionBudget = 10000;
            script.TickCoroutines(F64.FromInt(1));

            Assert.AreEqual(0, script.ActiveCoroutineCount);
            Assert.IsFalse(script.ExceptionContext.IsRaised());
            Assert.IsTrue(output.Count > 0 && output[0].Contains("Instruction budget exceeded"));
        }

        // ────────────────────────────────────────────────
        //  ensure across StopCoroutine
        // ────────────────────────────────────────────────

        [Test]
        public void StopCoroutine_MidYield_DoesNotRunEnsure()
        {
            // Stopping a coroutine discards its VM without resuming it, so an
            // ensure block spanning the yield never executes. Pinned behavior:
            // scripts must not rely on ensure for cleanup across yields when
            // the host may stop them.
            var (script, output) = TestHelper.Run("co_stop_ensure", @"
fun co []
    begin
        print ""in""
        yield
        print ""resumed""
    ensure
        print ""ensured""
    end
end
");
            var id = script.StartCoroutine("co", null);
            script.StopCoroutine(id);

            Assert.AreEqual(1, output.Count);
            Assert.AreEqual("in", output[0]);
        }

        // ────────────────────────────────────────────────
        //  dt edge cases
        // ────────────────────────────────────────────────

        [Test]
        public void ZeroDtTick_DoesNotFireWait()
        {
            var (script, output) = TestHelper.Run("co_zero_dt", @"
fun co []
    print ""start""
    yield wait 1.0
    print ""done""
end
");
            script.StartCoroutine("co", null);
            script.TickCoroutines(F64.Zero);

            Assert.AreEqual(1, output.Count, "wait 1.0 must not fire on a zero-dt tick");
            Assert.AreEqual(1, script.ActiveCoroutineCount);

            script.TickCoroutines(F64.FromInt(1));
            Assert.AreEqual("done", output[1]);
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        [Test]
        public void NegativeWaitDuration_FiresOnNextTick()
        {
            var (script, output) = TestHelper.Run("co_neg_wait", @"
fun co []
    yield wait -1
    print ""fired""
end
");
            script.StartCoroutine("co", null);
            script.TickCoroutines(F64.Zero);

            Assert.AreEqual("fired", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Locals across waits
        // ────────────────────────────────────────────────

        [Test]
        public void LoopLocalsAccumulateAcrossRepeatedWaits()
        {
            var (script, output) = TestHelper.Run("co_locals", @"
fun co []
    total = 0
    loop i in 0..3
        total += i + 10
        yield wait 0.5
    end
    print ""total "" + total
end
");
            script.StartCoroutine("co", null);
            for (var t = 0; t < 10; t++)
                script.TickCoroutines(F64.Half);

            Assert.AreEqual("total 33", output[0]); // 10 + 11 + 12
            Assert.AreEqual(0, script.ActiveCoroutineCount);
        }

        // ────────────────────────────────────────────────
        //  Host API failure modes
        // ────────────────────────────────────────────────

        [Test]
        public void StopCoroutine_UnknownId_ReturnsFalse()
        {
            var (script, _) = TestHelper.Run("co_bogus_stop", "fun co []\n    yield\nend\n");
            Assert.IsFalse(script.StopCoroutine(999));
        }

        [Test]
        public void StartCoroutine_WrongArity_ReturnsMinusOneAndRaises()
        {
            var (script, _) = TestHelper.Run("co_wrong_arity", @"
fun co [a]
    yield
end
");
            var id = script.StartCoroutine("co", null); // declared with 1 arg, given 0

            Assert.AreEqual(-1, id);
            Assert.IsTrue(script.ExceptionContext.IsRaised());
            script.ExceptionContext.RescueException();
        }

        // ────────────────────────────────────────────────
        //  yield outside a coroutine
        // ────────────────────────────────────────────────

        [Test]
        public void PlainCallHittingYield_StopsAtYield_AndSetsIsYielded()
        {
            var (script, output) = TestHelper.Run("plain_yield", @"
fun gen []
    print ""a""
    yield
    print ""b""
end
");
            script.Call(script.GetFunction("gen", 0));

            Assert.IsTrue(script.IsYielded);
            Assert.AreEqual(1, output.Count, "execution stops at the yield; there is no way to resume a plain Call");
            Assert.AreEqual("a", output[0]);
        }

        // ────────────────────────────────────────────────
        //  Debugger inside coroutines
        // ────────────────────────────────────────────────

        [Test]
        public void BreakpointsFireInsideCoroutineBody_AcrossYield()
        {
            var hitLines = new List<int>();
            var (script, _) = TestHelper.Run("co_debug", @"
fun co []
    a = 1
    yield
    b = 2
end
", setupScope: (s, _) =>
            {
                s.AddBreakpoint(3); // a = 1
                s.AddBreakpoint(5); // b = 2
                s.DebugHook = ctx => { hitLines.Add(ctx.Line); ctx.Action = StepMode.Continue; };
            });

            script.StartCoroutine("co", null); // runs to the yield → hits line 3
            script.TickCoroutines(F64.FromInt(1)); // resumes → hits line 5

            Assert.AreEqual(new[] { 3, 5 }, hitLines);
        }
    }
}
