using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression.Value;
using WarScript.Native;

namespace Tests
{
    [TestFixture]
    public class TailCallOptimizationTests
    {
        // ── Deep recursion that would stack-overflow without TCO ──

        [Test]
        public void DeepTailRecursionDoesNotOverflow()
        {
            // 10,000 recursive calls — would hit the 128-frame limit without TCO
            var (_, output) = TestHelper.Run("test", @"
                fun countdown [n]
                    if n <= 0
                        return ""done""
                    end
                    return countdown [n - 1]
                end
                print countdown [10000]
            ");
            Assert.AreEqual(new[] { "done" }, output);
        }

        [Test]
        public void MutualTailRecursion()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun is_even [n]
                    if n == 0
                        return true
                    end
                    return is_odd [n - 1]
                end
                fun is_odd [n]
                    if n == 0
                        return false
                    end
                    return is_even [n - 1]
                end
                print is_even [10000]
                print is_odd [10001]
            ");
            Assert.AreEqual(new[] { "True", "True" }, output);
        }

        [Test]
        public void TailRecursiveAccumulator()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun sum_acc [n, acc]
                    if n <= 0
                        return acc
                    end
                    return sum_acc [n - 1, acc + n]
                end
                print sum_acc [1000, 0]
            ");
            Assert.AreEqual(new[] { "500500" }, output);
        }

        [Test]
        public void TailRecursiveWithStringAccumulator()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun build [n, s]
                    if n <= 0
                        return s
                    end
                    return build [n - 1, s + ""x""]
                end
                result = build [200, """"]
                print result
            ");
            Assert.AreEqual(1, output.Count);
            Assert.AreEqual(200, output[0].Length);
        }

        // ── Non-tail calls still work correctly ──

        [Test]
        public void NonTailRecursionStillWorks()
        {
            // n * factorial[n-1] is NOT a tail call (multiplication wraps the call)
            var (_, output) = TestHelper.Run("test", @"
                fun factorial [n]
                    if n <= 1
                        return 1
                    end
                    return n * factorial [n - 1]
                end
                print factorial [10]
            ");
            Assert.AreEqual(new[] { "3628800" }, output);
        }

        [Test]
        public void FibonacciNonTailStillWorks()
        {
            // fib[n-1] + fib[n-2] is NOT a tail call
            var (_, output) = TestHelper.Run("test", @"
                fun fib [n]
                    if n <= 1
                        return n
                    end
                    return fib [n - 1] + fib [n - 2]
                end
                print fib [10]
            ");
            Assert.AreEqual(new[] { "55" }, output);
        }

        // ── Tail call to native function ──

        [Test]
        public void TailCallToNativeFunction()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun get_max [a, b]
                    return max [a, b]
                end
                print get_max [5, 10]
            ", delegate(WarScriptLanguage script, WarScript.Context.Definition.DefinitionScope scope)
            {
                MathLibrary.Register(script, scope);
            });
            Assert.AreEqual(new[] { "10" }, output);
        }

        // ── Safety: TCO disabled inside try/rescue ──

        [Test]
        public void ReturnInsideTryIsNotOptimized()
        {
            // If TCO fired inside begin/ensure, the ensure block would be skipped.
            // The compiler must emit a normal Call + Return here, not TailCall.
            var (_, output) = TestHelper.Run("test", @"
                result = ""none""
                fun risky [n]
                    begin
                        if n <= 0
                            return ""base""
                        end
                        return risky [n - 1]
                    ensure
                        result = ""ensured""
                    end
                end
                print risky [3]
                print result
            ");
            Assert.AreEqual(new[] { "base", "ensured" }, output);
        }

        [Test]
        public void TailCallOutsideTryStillOptimized()
        {
            // The tail call is OUTSIDE the begin/ensure block, so TCO should apply.
            // 10,000 deep — would overflow without TCO.
            var (_, output) = TestHelper.Run("test", @"
                fun deep [n]
                    if n <= 0
                        return ""done""
                    end
                    return deep [n - 1]
                end
                result = ""none""
                begin
                    result = deep [10000]
                ensure
                    print ""ensured""
                end
                print result
            ");
            Assert.AreEqual(new[] { "ensured", "done" }, output);
        }

        // ── Tail call preserves correct return value ──

        [Test]
        public void TailCallReturnValueCorrect()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun inner [x]
                    return x * 2
                end
                fun outer [x]
                    return inner [x + 1]
                end
                print outer [5]
            ");
            Assert.AreEqual(new[] { "12" }, output);
        }

        [Test]
        public void TailCallChainReturnValueCorrect()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun c [x]
                    return x + "" c""
                end
                fun b [x]
                    return c [x + "" b""]
                end
                fun a [x]
                    return b [x + "" a""]
                end
                print a [""start""]
            ");
            Assert.AreEqual(new[] { "start a b c" }, output);
        }

        // ── State machine pattern (the real use case) ──

        [Test]
        public void StateMachinePattern()
        {
            var (_, output) = TestHelper.Run("test", @"
                fun state_idle [ticks]
                    if ticks <= 0
                        return ""finished""
                    end
                    if ticks % 3 == 0
                        return state_attack [ticks - 1]
                    end
                    return state_idle [ticks - 1]
                end
                fun state_attack [ticks]
                    if ticks <= 0
                        return ""finished""
                    end
                    return state_idle [ticks - 1]
                end
                print state_idle [9999]
            ");
            Assert.AreEqual(new[] { "finished" }, output);
        }
    }
}
