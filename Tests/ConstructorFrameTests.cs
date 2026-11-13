using System;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// Constructors run as frames on the current VM (plan T1.1): no VM is
    /// created per instance (PERF-1), constructor bodies count against the
    /// caller's budgets (BUDGET-1) and frame limit (CRASH-2). The language
    /// side (order, rescue, cycles) is in Golden~/language/constructor_frames.ws.
    /// </summary>
    [TestFixture]
    public class ConstructorFrameTests
    {
        private const string Source = @"
class P [v]
    w = v + 1
end
fun make [n]
    loop i in 0..n
        p = new P [i]
    end
    return p :: w
end
";

        [Test]
        [Category("HeadlessOnly")]
        public void InstanceWithConstructorBody_AllocatesLessThan1KB()
        {
            if (Tests.Golden.IsolatedRun.RunnerPath() == null)
                Assert.Ignore("Allocation counting is measured in the headless build only (plan T1.1).");

            var (script, output) = TestHelper.Run("t", Source);
            var make = script.GetFunction("make", 1);
            script.Call(make, WarValue.FromNumeric(FixMath.F64.FromInt(100))); // warm up

            const int count = 20_000;
            var before = GC.GetAllocatedBytesForCurrentThread();
            script.Call(make, WarValue.FromNumeric(FixMath.F64.FromInt(count)));
            var perInstance = (GC.GetAllocatedBytesForCurrentThread() - before) / count;

            CollectionAssert.IsEmpty(output);
            TestContext.Progress.WriteLine("T1.1: " + perInstance + " bytes allocated per instance");
            Assert.Less(perInstance, 1024, "bytes allocated per instance");
        }

        [Test]
        public void ConstructorBody_ChargesTheCallersInstructionBudget()
        {
            // 'make [n]' with n instances runs a few instructions per
            // constructor body; with one VM per constructor (v1) only the
            // loop in 'make' was charged.
            var (script, output) = TestHelper.Run("t", Source + @"
make [2000]
print ""done""", (s, _) => s.InstructionBudget = 10_000);
            CollectionAssert.DoesNotContain(output, "done");
            Assert.AreEqual("Instruction budget exceeded", script.ExceptionContext.LastUnhandled?.Value.ToString());
        }

        [Test]
        public void ConstructorBody_ChargesTheCallersMemoryBudget()
        {
            // Each constructor body builds a 64-element array (64 * 16 + 64
            // estimated bytes); 20 of them exceed a 16 KB budget.
            var (_, output) = TestHelper.Run("t", @"
class Q [n]
    data = {}
    loop i in 0..64
        data << i
    end
end
begin
    loop j in 0..20
        q = new Q [j]
    end
    print ""done""
rescue e
    print e
end", (s, _) => s.MemoryBudget = 16_384);
            CollectionAssert.AreEqual(new[] { "Memory budget exceeded" }, output);
        }

        [Test]
        public void FramesThatDoNotFit_RaiseStackOverflowBeforeAnyConstructorRuns()
        {
            // A chain of 130 classes needs 130 constructor frames at once,
            // more than FramesMax (128): nothing runs, the error is rescuable.
            var src = new System.Text.StringBuilder("log = {}\nclass C0\n    log << 0\nend\n");
            for (int i = 1; i < 130; i++)
                src.Append("class C" + i + " : C" + (i - 1) + "\n    log << " + i + "\nend\n");
            src.Append(@"
begin
    c = new C129
rescue e
    print e
end
print log");
            var (_, output) = TestHelper.Run("t", src.ToString());
            CollectionAssert.AreEqual(new[] { "Stack overflow", "[]" }, output);
        }
    }
}
