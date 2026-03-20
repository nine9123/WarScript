using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace Tests
{
    /// <summary>
    /// Performance benchmarks for WarScript, split by module and operation type.
    ///
    /// Each benchmark runs the operation multiple times and reports timing stats.
    /// Use these to measure before/after when making performance changes.
    ///
    /// Run with: Unity Test Runner or `dotnet test --filter TestCategory=Benchmark`
    /// </summary>
    [TestFixture]
    [Category("Benchmark")]
    public class Benchmarks
    {
        // ── Configuration ──

        // Warmup runs to let JIT settle before measuring
        private const int WarmupRuns = 3;

        // Measured iterations for each benchmark
        private const int MeasuredRuns = 20;

        // ── Helpers ──

        private struct BenchmarkResult
        {
            public string Name;
            public double[] TimingsMs;
            public double MinMs;
            public double MaxMs;
            public double AvgMs;
            public double MedianMs;
            public double StdDevMs;
        }

        private static BenchmarkResult Measure(
            string name,
            Action action,
            int warmup = WarmupRuns,
            int runs = MeasuredRuns)
        {
            // Warmup
            for (var i = 0; i < warmup; i++)
                action();

            // Force GC before measuring to reduce noise
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var timings = new double[runs];
            var sw = new Stopwatch();

            for (var i = 0; i < runs; i++)
            {
                sw.Restart();
                action();
                sw.Stop();
                timings[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(timings);

            var avg = timings.Average();
            var sumSqDiff = timings.Sum(t => (t - avg) * (t - avg));

            var result = new BenchmarkResult
            {
                Name = name,
                TimingsMs = timings,
                MinMs = timings[0],
                MaxMs = timings[^1],
                AvgMs = avg,
                MedianMs = timings[runs / 2],
                StdDevMs = Math.Sqrt(sumSqDiff / runs)
            };

            Report(result);
            return result;
        }

        private static readonly StringBuilder ResultLog = new StringBuilder();
        
        private static void Report(BenchmarkResult r)
        {
            var line = $"[{r.Name}]  " +
                       $"\nmin={r.MinMs:F3}ms  " +
                       $"\nmedian={r.MedianMs:F3}ms  " +
                       $"\navg={r.AvgMs:F3}ms  " +
                       $"\nmax={r.MaxMs:F3}ms  " +
                       $"\nstddev={r.StdDevMs:F3}ms";

            TestContext.WriteLine(line);
            ResultLog.AppendLine(line);
        }

        [OneTimeSetUp]
        public void Setup()
        {
            ResultLog.Clear();
        }
        
        [OneTimeTearDown]
        public void ExportResults()
        {
            if (ResultLog.Length == 0) return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"warscript_bench_{timestamp}.txt");

            File.WriteAllText(path, ResultLog.ToString());
            TestContext.WriteLine($"Results saved to: {path}");
        }

        private static (WarScriptLanguage script, List<string> output) Run(string source)
        {
            return TestHelper.Run(source);
        }

        /// <summary>
        /// Generates a large WarScript source string for lexer/parser stress tests.
        /// </summary>
        private static string GenerateSource(int lines)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < lines; i++)
                sb.AppendLine($"x_{i} = {i} + {i * 2} * {i + 1}");
            return sb.ToString();
        }

        // =====================================================================
        //  MODULE 1: LEXER
        // =====================================================================

        [Test]
        public void Lexer_SmallSource()
        {
            var source = "x = 2 + 3 * (4 - 1)\nprint x";
            Measure("Lexer: small (2 lines)", () =>
            {
                LexicalParser.Parse(source);
            });
        }

        [Test]
        public void Lexer_MediumSource()
        {
            var source = GenerateSource(100);
            Measure("Lexer: medium (100 lines)", () =>
            {
                LexicalParser.Parse(source);
            });
        }

        [Test]
        public void Lexer_LargeSource()
        {
            var source = GenerateSource(1000);
            Measure("Lexer: large (1000 lines)", () =>
            {
                LexicalParser.Parse(source);
            });
        }

        [Test]
        public void Lexer_StringHeavy()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 500; i++)
                sb.AppendLine($"s_{i} = \"string literal number {i} with some padding text\"");
            var source = sb.ToString();

            Measure("Lexer: string-heavy (500 lines)", () =>
            {
                LexicalParser.Parse(source);
            });
        }

        [Test]
        public void Lexer_OperatorHeavy()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 500; i++)
                sb.AppendLine($"x = {i} + {i} - {i} * {i} / {i + 1} % {i + 1} ** 2 == {i} != {i} >= {i} <= {i}");
            var source = sb.ToString();

            Measure("Lexer: operator-heavy (500 lines)", () =>
            {
                LexicalParser.Parse(source);
            });
        }

        // =====================================================================
        //  MODULE 2: PARSER (AST creation)
        // =====================================================================

        [Test]
        public void Parser_FlatStatements()
        {
            var source = GenerateSource(200);
            var tokens = LexicalParser.Parse(source);

            Measure("Parser: flat statements (200 lines)", () =>
            {
                var script = new WarScriptLanguage("bench", "", null, null);
                script.Run();
                script.DefinitionContext.PushScope(script.DefinitionContext.NewScope());
                script.MemoryContext.PushScope(script.MemoryContext.NewScope());
                try
                {
                    var statement = new CompositeStatement(script, null, "bench");
                    StatementParser.Parse(script, tokens, statement);
                }
                finally
                {
                    script.DefinitionContext.EndScope();
                    script.MemoryContext.EndScope();
                }
            });
        }

        [Test]
        public void Parser_NestedConditions()
        {
            var sb = new StringBuilder();
            sb.AppendLine("x = 5");
            for (var i = 0; i < 50; i++)
            {
                sb.AppendLine($"if x > {i}");
                sb.AppendLine($"    y_{i} = x + {i}");
                sb.AppendLine("end");
            }
            var source = sb.ToString();
            var tokens = LexicalParser.Parse(source);

            Measure("Parser: nested conditions (50 if blocks)", () =>
            {
                var script = new WarScriptLanguage("bench", "", null, null);
                script.Run();
                script.DefinitionContext.PushScope(script.DefinitionContext.NewScope());
                script.MemoryContext.PushScope(script.MemoryContext.NewScope());
                try
                {
                    var statement = new CompositeStatement(script, null, "bench");
                    StatementParser.Parse(script, tokens, statement);
                }
                finally
                {
                    script.DefinitionContext.EndScope();
                    script.MemoryContext.EndScope();
                }
            });
        }

        [Test]
        public void Parser_ManyFunctions()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 100; i++)
            {
                sb.AppendLine($"fun func_{i} [a, b]");
                sb.AppendLine($"    return a + b + {i}");
                sb.AppendLine("end");
            }
            var source = sb.ToString();
            var tokens = LexicalParser.Parse(source);

            Measure("Parser: function definitions (100 functions)", () =>
            {
                var script = new WarScriptLanguage("bench", "", null, null);
                script.Run();
                script.DefinitionContext.PushScope(script.DefinitionContext.NewScope());
                script.MemoryContext.PushScope(script.MemoryContext.NewScope());
                try
                {
                    var statement = new CompositeStatement(script, null, "bench");
                    StatementParser.Parse(script, tokens, statement);
                }
                finally
                {
                    script.DefinitionContext.EndScope();
                    script.MemoryContext.EndScope();
                }
            });
        }

        // =====================================================================
        //  MODULE 3: FULL PIPELINE (Lex + Parse + Execute)
        // =====================================================================

        [Test]
        public void FullPipeline_SmallScript()
        {
            var source = @"
                x = 10
                y = 20
                z = x + y
            ";
            Measure("Full pipeline: small (3 statements)", () => { Run(source); });
        }

        [Test]
        public void FullPipeline_MediumScript()
        {
            var source = GenerateSource(200);
            Measure("Full pipeline: medium (200 assignments)", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 4: EXECUTION — Loops
        // =====================================================================

        [Test]
        public void Exec_WhileLoop_10k()
        {
            var source = @"
                i = 0
                loop i < 10000
                    i = i + 1
                end
            ";
            Measure("Exec: while loop 10k iterations", () => { Run(source); });
        }

        [Test]
        public void Exec_ForLoop_10k()
        {
            var source = @"
                sum = 0
                loop i in 0..10000
                    sum = sum + i
                end
            ";
            Measure("Exec: for loop 10k iterations", () => { Run(source); });
        }

        [Test]
        public void Exec_ForLoop_WithStep()
        {
            var source = @"
                sum = 0
                loop i in 0..30000 by 3
                    sum = sum + i
                end
            ";
            Measure("Exec: for loop 10k iterations (step 3)", () => { Run(source); });
        }

        [Test]
        public void Exec_NestedLoops()
        {
            var source = @"
                sum = 0
                loop i in 0..100
                    loop j in 0..100
                        sum = sum + 1
                    end
                end
            ";
            Measure("Exec: nested loops 100x100", () => { Run(source); });
        }

        [Test]
        public void Exec_LoopWithBreak()
        {
            var source = @"
                loop i in 0..100000
                    if i == 5000
                        break
                    end
                end
            ";
            Measure("Exec: loop with break at 5000", () => { Run(source); });
        }

        [Test]
        public void Exec_LoopWithNext()
        {
            var source = @"
                sum = 0
                loop i in 0..10000
                    if i % 2 == 0
                        next
                    end
                    sum = sum + i
                end
            ";
            Measure("Exec: loop with next (skip evens) 10k", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 5: EXECUTION — Arithmetic
        // =====================================================================

        [Test]
        public void Exec_Addition_10k()
        {
            var source = @"
                sum = 0
                loop i in 0..10000
                    sum = sum + i
                end
            ";
            Measure("Exec: addition 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_Multiplication_10k()
        {
            var source = @"
                result = 1
                loop i in 1..10000
                    result = i * 2
                end
            ";
            Measure("Exec: multiplication 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_MixedArithmetic_10k()
        {
            var source = @"
                result = 0
                loop i in 1..10000
                    result = (i + 2) * 3 - 1
                end
            ";
            Measure("Exec: mixed arithmetic 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_FloorDivAndModulo_10k()
        {
            var source = @"
                result = 0
                loop i in 1..10000
                    result = i // 3 + i % 7
                end
            ";
            Measure("Exec: floor div + modulo 10k", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 6: EXECUTION — Functions
        // =====================================================================

        [Test]
        public void Exec_FunctionCall_10k()
        {
            var source = @"
                fun add [a, b]
                    return a + b
                end
                sum = 0
                loop i in 0..10000
                    sum = add [sum, i]
                end
            ";
            Measure("Exec: function call 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_Recursion_Fibonacci()
        {
            var source = @"
                fun fib [n]
                    if n < 2
                        return n
                    end
                    return fib [n - 1] + fib [n - 2]
                end
                result = fib [20]
            ";
            Measure("Exec: recursive fibonacci(20)", () => { Run(source); });
        }

        [Test]
        public void Exec_ManyFunctionDefinitions_Then_Call()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 200; i++)
            {
                sb.AppendLine($"fun f_{i} [x]");
                sb.AppendLine($"    return x + {i}");
                sb.AppendLine("end");
            }
            // Call the last one to force lookup across all definitions
            sb.AppendLine("result = f_199 [1]");
            var source = sb.ToString();

            Measure("Exec: 200 function defs + lookup last", () => { Run(source); });
        }

        [Test]
        public void Exec_NativeFunctionCall_10k()
        {
            var source = @"
                sum = 0
                loop i in 0..10000
                    sum = native_add [sum, i]
                end
            ";
            Measure("Exec: native function call 10k", () =>
            {
                TestHelper.Run(source, delegate(DefinitionScope scope)
                {
                    scope.AddFunction(new NativeFunctionDefinition(
                        new FunctionDetails("native_add", new List<string> { "a", "b" }),
                        args =>
                        {
                            var a = NativeHelper.Arg<NumericValue>(args, 0);
                            var b = NativeHelper.Arg<NumericValue>(args, 1);
                            return new NumericValue(null, a.GetValue() + b.GetValue());
                        },
                        "", ""));
                });
            });
        }

        // =====================================================================
        //  MODULE 7: EXECUTION — Class instances
        // =====================================================================

        [Test]
        public void Exec_ClassCreation_1k()
        {
            var source = @"
                class Point [x, y]
                end
                loop i in 0..1000
                    p = new Point [i, i + 1]
                end
            ";
            Measure("Exec: class creation 1k", () => { Run(source); });
        }

        [Test]
        public void Exec_ClassPropertyRead_10k()
        {
            var source = @"
                class Point [x, y]
                end
                p = new Point [3, 4]
                sum = 0
                loop i in 0..10000
                    sum = sum + p :: x
                end
            ";
            Measure("Exec: class property read 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_ClassPropertyWrite_10k()
        {
            var source = @"
                class Box [value]
                end
                b = new Box [0]
                loop i in 0..10000
                    b :: value = i
                end
            ";
            Measure("Exec: class property write 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_ClassMethodCall_5k()
        {
            var source = @"
                class Counter [n]
                    fun increment []
                        n = n + 1
                    end
                    fun get []
                        return this :: n
                    end
                end
                c = new Counter [0]
                loop i in 0..5000
                    c :: increment []
                end
            ";
            Measure("Exec: class method call 5k", () => { Run(source); });
        }

        [Test]
        public void Exec_ClassInheritance_MethodLookup()
        {
            var source = @"
                class A []
                    fun action []
                        return 42
                    end
                end
                class B [] : A []
                end
                class C [] : B []
                end
                obj = new C []
                sum = 0
                loop i in 0..5000
                    sum = sum + obj :: action []
                end
            ";
            Measure("Exec: inherited method call (3 deep) 5k", () => { Run(source); });
        }

        [Test]
        public void Exec_ClassCreationWithInheritance_500()
        {
            var source = @"
                class Base [a]
                end
                class Mid [a, b] : Base [a]
                end
                class Leaf [a, b, c] : Mid [a, b]
                end
                loop i in 0..500
                    obj = new Leaf [i, i + 1, i + 2]
                end
            ";
            Measure("Exec: inherited class creation (3 deep) 500", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 8: EXECUTION — Strings
        // =====================================================================

        [Test]
        public void Exec_StringConcat_5k()
        {
            var source = @"
                s = """"
                loop i in 0..5000
                    s = s + ""a""
                end
            ";
            Measure("Exec: string concatenation 5k", () => { Run(source); });
        }

        [Test]
        public void Exec_StringRepeat()
        {
            var source = @"
                loop i in 0..1000
                    s = ""abc"" * 100
                end
            ";
            Measure("Exec: string repeat (100x) 1k times", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 9: EXECUTION — Arrays
        // =====================================================================

        [Test]
        public void Exec_ArrayAppend_5k()
        {
            var source = @"
                arr = {}
                loop i in 0..5000
                    arr << i
                end
            ";
            Measure("Exec: array append 5k", () => { Run(source); });
        }

        [Test]
        public void Exec_ArrayIndexRead_10k()
        {
            // Build a 100-element array, then read from it in a tight loop
            var source = @"
                arr = {}
                loop i in 0..100
                    arr << i
                end
                sum = 0
                loop i in 0..10000
                    sum = sum + arr{i % 100}
                end
            ";
            Measure("Exec: array index read 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_ArrayIndexWrite_5k()
        {
            var source = @"
                arr = {}
                loop i in 0..100
                    arr << 0
                end
                loop i in 0..5000
                    arr{i % 100} = i
                end
            ";
            Measure("Exec: array index write 5k", () => { Run(source); });
        }

        [Test]
        public void Exec_ArrayConcat_1k()
        {
            var source = @"
                a = {1, 2, 3}
                b = {4, 5, 6}
                loop i in 0..1000
                    c = a + b
                end
            ";
            Measure("Exec: array concat 1k", () => { Run(source); });
        }

        [Test]
        public void Exec_ArrayIteration_1k()
        {
            var source = @"
                arr = {}
                loop i in 0..1000
                    arr << i
                end
                sum = 0
                loop item in arr
                    sum = sum + item
                end
            ";
            Measure("Exec: iterate 1000-element array", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 10: EXECUTION — Variable scope
        // =====================================================================

        [Test]
        public void Exec_VariableLookup_Shallow()
        {
            var source = @"
                x = 42
                sum = 0
                loop i in 0..10000
                    sum = sum + x
                end
            ";
            Measure("Exec: shallow variable lookup 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_VariableLookup_Deep()
        {
            // Variable defined in outer scope, read from inside nested conditions
            var source = @"
                x = 42
                sum = 0
                loop i in 0..5000
                    if true
                        if true
                            if true
                                sum = sum + x
                            end
                        end
                    end
                end
            ";
            Measure("Exec: deep scope variable lookup 5k (3 nested)", () => { Run(source); });
        }

        [Test]
        public void Exec_ManyLocalVariables()
        {
            var sb = new StringBuilder();
            sb.AppendLine("loop iteration in 0..1000");
            for (var i = 0; i < 50; i++)
                sb.AppendLine($"    v_{i} = {i}");
            sb.AppendLine("    sum = v_0 + v_49");
            sb.AppendLine("end");
            var source = sb.ToString();

            Measure("Exec: 50 local vars per iteration x1k", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 11: EXECUTION — Comparisons & Logic
        // =====================================================================

        [Test]
        public void Exec_Comparisons_10k()
        {
            var source = @"
                count = 0
                loop i in 0..10000
                    if i > 5000
                        count = count + 1
                    end
                end
            ";
            Measure("Exec: numeric comparisons 10k", () => { Run(source); });
        }

        [Test]
        public void Exec_LogicalOperators_10k()
        {
            var source = @"
                count = 0
                loop i in 0..10000
                    if i > 2000 and i < 8000
                        count = count + 1
                    end
                end
            ";
            Measure("Exec: logical and (short-circuit) 10k", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 12: EXECUTION — Exception handling
        // =====================================================================

        [Test]
        public void Exec_TryCatch_1k()
        {
            var source = @"
                loop i in 0..1000
                    begin
                        raise ""error""
                    rescue e
                        x = 1
                    end
                end
            ";
            Measure("Exec: raise + rescue 1k", () => { Run(source); });
        }

        [Test]
        public void Exec_TryCatchEnsure_1k()
        {
            var source = @"
                loop i in 0..1000
                    begin
                        raise ""error""
                    rescue e
                        x = 1
                    ensure
                        y = 1
                    end
                end
            ";
            Measure("Exec: raise + rescue + ensure 1k", () => { Run(source); });
        }

        // =====================================================================
        //  MODULE 13: CALL API (game engine tick simulation)
        // =====================================================================

        [Test]
        public void CallApi_TickLoop_10k()
        {
            var (script, _) = Run(@"
                counter = 0
                fun tick []
                    counter = counter + 1
                end
            ");
            var tick = script.GetFunction("tick", 0);

            Measure("Call API: tick() 10k invocations", () =>
            {
                for (var i = 0; i < 10000; i++)
                    script.Call(tick);
            });
        }

        [Test]
        public void CallApi_TickWithArgs_10k()
        {
            var (script, _) = Run(@"
                sum = 0
                fun tick [dt]
                    sum = sum + dt
                end
            ");
            var tick = script.GetFunction("tick", 1);
            var dt = new NumericValue(script, 0.016);

            Measure("Call API: tick(dt) 10k with arg", () =>
            {
                for (var i = 0; i < 10000; i++)
                    script.Call(tick, dt);
            });
        }

        [Test]
        public void CallApi_TickHeavy_1k()
        {
            // Simulates a realistic game tick: reads class state, does math, writes back
            var (script, _) = Run(@"
                class Entity [x, y, hp]
                end
                e = new Entity [0, 0, 100]

                fun tick [dx, dy]
                    e :: x = e :: x + dx
                    e :: y = e :: y + dy
                    dist = (e :: x ** 2 + e :: y ** 2) ** 0.5
                    if dist > 100
                        e :: hp = e :: hp - 1
                    end
                end
            ");
            var tick = script.GetFunction("tick", 2);
            var dx = new NumericValue(script, 0.5);
            var dy = new NumericValue(script, 0.3);

            Measure("Call API: heavy tick (class r/w + math) 1k", () =>
            {
                for (var i = 0; i < 1000; i++)
                    script.Call(tick, dx, dy);
            });
        }

        [Test]
        public void CallApi_TickHeavy_GCPressure_Sustained()
        {
            var (script, _) = Run(@"
                class Entity [x, y, hp]
                end
                e = new Entity [0, 0, 100]
                fun tick [dx, dy]
                    e :: x = e :: x + dx
                    e :: y = e :: y + dy
                    dist = (e :: x ** 2 + e :: y ** 2) ** 0.5
                    if dist > 100
                        e :: hp = e :: hp - 1
                    end
                end
            ");
            var tick = script.GetFunction("tick", 2);
            var dx = new NumericValue(script, 0.5);
            var dy = new NumericValue(script, 0.3);

            // Warmup
            for (var i = 0; i < 1000; i++)
                script.Call(tick, dx, dy);

            // Force full collection and measure from a clean baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memBefore = GC.GetTotalMemory(true); // true = force collection first

            const int iterations = 100000;
            for (var i = 0; i < iterations; i++)
                script.Call(tick, dx, dy);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memAfter = GC.GetTotalMemory(true);

            // Survived memory is what GC couldn't free: should be near zero for a tick loop.
            // The real allocation volume is much higher, but most is collected.
            // We measure by forcing collections at checkpoints.
            var survived = memAfter - memBefore;

            // More accurate: measure total allocation by running in chunks
            // and summing heap growth between forced collections.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            const int chunkSize = 10000;
            const int chunks = 10;
            long totalAllocated = 0;

            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var before = GC.GetTotalMemory(true);

                for (var i = 0; i < chunkSize; i++)
                    script.Call(tick, dx, dy);

                // Don't collect: measure raw heap growth
                var after = GC.GetTotalMemory(false);
                totalAllocated += (after - before);

                // Now collect to reset for next chunk
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            var bytesPerTick = totalAllocated / (double)(chunkSize * chunks);
            var totalTicks = chunkSize * chunks;

            TestContext.WriteLine($"--- Allocation Measurement ({totalTicks:N0} ticks, {chunks} chunks) ---");
            TestContext.WriteLine($"Bytes per tick: {bytesPerTick:F1}");
            TestContext.WriteLine($"Survived memory: {survived:N0} bytes");
            TestContext.WriteLine("");
            TestContext.WriteLine($"--- Projection: 200 entities @ 60fps ---");
            TestContext.WriteLine($"Per frame: {bytesPerTick * 200:F0} bytes");
            TestContext.WriteLine($"Per second: {bytesPerTick * 200 * 60 / 1024.0 / 1024.0:F2} MB");
            TestContext.WriteLine($"Gen0 collection roughly every: {4.0 * 1024 * 1024 / (bytesPerTick * 200 * 60):F1} seconds (assuming 4MB nursery)");
        }
        
        // =====================================================================
        //  MODULE 14: INTEGRATION — Script file benchmarks
        // =====================================================================

        private static string GetResourcePath(
            string resourceName,
            [CallerFilePath] string sourceFilePath = "")
        {
            var testDir = System.IO.Path.GetDirectoryName(sourceFilePath)!;
            var path = System.IO.Path.Combine(testDir, "resources", resourceName);
            if (System.IO.File.Exists(path))
                return path;
            throw new System.IO.FileNotFoundException(
                $"Test resource '{resourceName}' not found.");
        }

        private static void BenchmarkScriptFile(string resourceName)
        {
            var path = GetResourcePath(resourceName);
            var sourceCode = System.IO.File.ReadAllText(path);

            Measure($"Script file: {resourceName}", () =>
            {
                new WarScriptLanguage(resourceName, sourceCode, null, null).Run();
            });
        }

        [Test]
        public void Script_BinarySearch()
        {
            BenchmarkScriptFile("binary_search.ws");
        }

        [Test]
        public void Script_BubbleSort()
        {
            BenchmarkScriptFile("bubble_sort.ws");
        }

        [Test]
        public void Script_Calculator()
        {
            BenchmarkScriptFile("calculator.ws");
        }

        [Test]
        public void Script_IsSameTree()
        {
            BenchmarkScriptFile("is_same_tree.ws");
        }

        [Test]
        public void Script_Stack()
        {
            BenchmarkScriptFile("stack.ws");
        }
    }
}