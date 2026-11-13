using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Context.Definition;

namespace Tests
{
    /// <summary>
    /// T1.10 (DX-3): every instruction the compiler emits for an expression
    /// carries the line of its statement, so the debugger's lines and call
    /// stacks (and, from T2.5, error lines) are right. Every golden's
    /// compiled code is checked by
    /// <see cref="BytecodeFormatTests.CompiledGolden_EveryInstructionHasALine"/>.
    /// </summary>
    [TestFixture]
    public class LineInfoTests
    {
        // Each instruction family on a line of its own; the comment is the line.
        private const string Source =
            "fun twice [x]\n" +                         // 1
            "    return x * 2\n" +                      // 2
            "end\n" +                                   // 3
            "fun apply [g, v]\n" +                      // 4
            "    return g [v]\n" +                      // 5  CallValue
            "end\n" +                                   // 6
            "class Box [value]\n" +                     // 7
            "    fun get []\n" +                        // 8
            "        return this :: value\n" +          // 9  ThisGetProperty
            "    end\n" +                               // 10
            "    class Inner [n]\n" +                   // 11
            "    end\n" +                               // 12
            "end\n" +                                   // 13
            "a = 10\n" +                                // 14
            "b = twice [a]\n" +                         // 15 Call
            "box = new Box [a]\n" +                     // 16 NewInstance
            "inner = box :: new Inner [1]\n" +          // 17 NewNestedInstance
            "c = box :: get []\n" +                     // 18 CallMethod
            "d = box :: value\n" +                      // 19 GetProperty
            "arr = {1, 2, 3}\n" +                       // 20 NewArray
            "e = arr{1}\n" +                            // 21 IndexGet
            "f = a / b\n" +                             // 22 Div
            "g = a > 1 and b > 1\n" +                   // 23 JumpIfFalse (and)
            "h = box is Box\n" +                        // 24 InstanceOf
            "i = box as Box\n" +                        // 25 CastAs
            "j = apply [fun [y] return y end, 3]\n" +   // 26 Call, lambda constant
            "print a + b\n";                            // 27 Add

        private static (CompiledFunction body, DefinitionScope definitions) Compile(string source)
        {
            return new WarScriptLanguage("lines", source, null, null).CompileWithoutRunning();
        }

        /// <summary>Lines of every <paramref name="op"/> instruction in <paramref name="function"/>.</summary>
        private static List<int> LinesOf(CompiledFunction function, OpCode op)
        {
            var chunk = function.Chunk;
            return chunk.InstructionStarts()
                .Where(at => chunk.Code[at] == (byte)op)
                .Select(at => chunk.Lines.GetLine(at))
                .ToList();
        }

        private static CompiledFunction Function(DefinitionScope scope, string name)
        {
            return scope.Functions.First(f => f.Details.Name == name).Compiled;
        }

        [TestCase(OpCode.Call, 15, 26)]
        [TestCase(OpCode.NewInstance, 16)]
        [TestCase(OpCode.NewNestedInstance, 17)]
        [TestCase(OpCode.CallMethod, 18)]
        [TestCase(OpCode.GetProperty, 19)]
        [TestCase(OpCode.NewArray, 20)]
        [TestCase(OpCode.IndexGet, 21)]
        [TestCase(OpCode.Div, 22)]
        [TestCase(OpCode.JumpIfFalse, 23)]
        [TestCase(OpCode.InstanceOf, 24)]
        [TestCase(OpCode.CastAs, 25)]
        [TestCase(OpCode.Add, 27)]
        public void ScriptBody_InstructionHasItsStatementLine(OpCode op, params int[] lines)
        {
            var (body, _) = Compile(Source);
            CollectionAssert.AreEqual(lines, LinesOf(body, op), op.ToString());
        }

        [Test]
        public void Operands_HaveTheLineOfTheirStatement()
        {
            // The loads feeding `f = a / b` (line 22) are on line 22 too, not 0.
            var (body, _) = Compile(Source);
            var chunk = body.Chunk;
            var starts = chunk.InstructionStarts();
            var div = starts.Single(at => chunk.Code[at] == (byte)OpCode.Div);
            var index = starts.IndexOf(div);
            Assert.AreEqual(22, chunk.Lines.GetLine(starts[index - 1]), "load of b");
            Assert.AreEqual(22, chunk.Lines.GetLine(starts[index - 2]), "load of a");
        }

        [Test]
        public void FunctionsAndMethods_HaveTheirStatementLines()
        {
            var (_, definitions) = Compile(Source);
            CollectionAssert.AreEqual(new[] { 5 }, LinesOf(Function(definitions, "apply"), OpCode.CallValue));
            CollectionAssert.AreEqual(new[] { 2 }, LinesOf(Function(definitions, "twice"), OpCode.Mul));
            var box = definitions.ClassDefinitions.First(c => c.ClassDetails.Name == "Box");
            var get = box.GetDefinitionScope().Functions.First(f => f.Details.Name == "get").Compiled;
            CollectionAssert.AreEqual(new[] { 9 }, LinesOf(get, OpCode.ThisGetProperty));
        }

        [Test]
        public void MultiLineCall_UsesTheStatementsFirstLine()
        {
            var (body, _) = Compile("x = 1\ny = max [\n    x,\n    2\n]\n");
            CollectionAssert.AreEqual(new[] { 2 }, LinesOf(body, OpCode.Call));
        }

        [Test]
        public void DebuggerCallStack_ShowsTheCallersLine()
        {
            // Before T1.10 the caller's frame stood on the Call instruction,
            // which was emitted at line 0.
            var stacks = new List<string>();
            TestHelper.Run("test",
                "fun inner [x]\n" +      // 1
                "    y = x + 1\n" +      // 2
                "    return y\n" +       // 3
                "end\n" +                // 4
                "a = 1\n" +              // 5
                "b = inner [a] * 2\n" +  // 6
                "print b\n",             // 7
                setupScope: (s, _) =>
                {
                    s.AddBreakpoint(2);
                    s.DebugHook = ctx =>
                    {
                        stacks.Add(string.Join(" > ", ctx.CallStack.Select(e => e.ToString())));
                        ctx.Action = StepMode.Continue;
                    };
                });
            CollectionAssert.AreEqual(new[] { "<script>:6 > inner:2" }, stacks);
        }

        [Test]
        public void DebuggerCallStack_ShowsTheLineOfANew()
        {
            var stacks = new List<string>();
            TestHelper.Run("test",
                "class P [x]\n" +        // 1
                "    y = x * 2\n" +      // 2
                "end\n" +                // 3
                "a = 1\n" +              // 4
                "p = new P [a]\n",       // 5
                setupScope: (s, _) =>
                {
                    s.AddBreakpoint(2);
                    s.DebugHook = ctx =>
                    {
                        stacks.Add(string.Join(" > ", ctx.CallStack.Select(e => e.ToString())));
                        ctx.Action = StepMode.Continue;
                    };
                });
            CollectionAssert.AreEqual(new[] { "<script>:5 > P#ctor:2" }, stacks);
        }
    }
}
