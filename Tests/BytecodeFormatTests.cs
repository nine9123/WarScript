using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using WarScript;
using WarScript.Bytecode;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

namespace Tests
{
    /// <summary>
    /// The opcode table, the format header and the table-driven disassembler
    /// (v2 plan T1.7, 10.1, 10.2; DET-4, SEM-10, DX-5).
    /// </summary>
    [TestFixture]
    public class BytecodeFormatTests
    {
        // ── The opcode table ────────────────────────────────────────────

        [Test]
        public void FormatHash_MatchesTheOpcodeTable()
        {
            var actual = BytecodeFormat.ComputeHash();
            Assert.AreEqual(BytecodeFormat.Hash, actual,
                "opcode table changed: bump BytecodeFormat.Version and update BytecodeFormat.Hash (computed: 0x"
                + actual.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        [Test]
        public void Table_HasOneEntryPerOpcode_InIncreasingOrder()
        {
            var values = (OpCode[])Enum.GetValues(typeof(OpCode));
            CollectionAssert.AreEqual(values.OrderBy(v => (byte)v).ToArray(),
                OpInfo.Table.Select(i => i.Op).ToArray());
            foreach (var op in values)
                Assert.AreSame(OpInfo.Table.Single(i => i.Op == op), OpInfo.Get((byte)op), op.ToString());
        }

        [Test]
        public void RemovedOpcodes_AreNotDefined()
        {
            // 58 was YieldUntil, 61 to 65 the fused compare-and-jumps the
            // compiler never emitted (SEM-10). Their values are never reused.
            foreach (var value in new byte[] { 58, 61, 62, 63, 64, 65 })
            {
                Assert.IsFalse(Enum.IsDefined(typeof(OpCode), value), value.ToString());
                Assert.IsNull(OpInfo.Get(value));
            }
        }

        [Test]
        public void VariableStackEffects_AreTheOnesOf10_1()
        {
            var variable = OpInfo.Table.Where(i => i.Pops == OpInfo.Variable).Select(i => i.Op).ToArray();
            CollectionAssert.AreEquivalent(new[]
            {
                OpCode.PopN, OpCode.Call, OpCode.TailCall, OpCode.CallValue, OpCode.NewArray,
                OpCode.NewInstance, OpCode.NewNestedInstance, OpCode.CallMethod,
            }, variable);
        }

        // ── Header ──────────────────────────────────────────────────────

        private static byte[] SavedBytecode()
        {
            var script = new WarScriptLanguage("t", "fun f []\n    return 1\nend\nprint f []", null, null);
            var ms = new MemoryStream();
            script.SaveBytecode(ms);
            return ms.ToArray();
        }

        private static void Load(byte[] bytes) =>
            new WarScriptLanguage("t", "", null, null).LoadBytecode(new MemoryStream(bytes));

        [Test]
        public void Header_CarriesVersionAndFormatHash()
        {
            var bytes = SavedBytecode();
            Assert.AreEqual(BytecodeFormat.Version, bytes[4]);
            Assert.AreEqual(BytecodeFormat.Hash, BitConverter.ToUInt32(bytes, 5));
            Assert.DoesNotThrow(() => Load(bytes));
        }

        [Test]
        public void Load_RejectsAnotherVersion()
        {
            foreach (byte version in new byte[] { 1, 3 })
            {
                var bytes = SavedBytecode();
                bytes[4] = version;
                var e = Assert.Throws<InvalidDataException>(() => Load(bytes));
                StringAssert.Contains("version", e.Message);
            }
        }

        [Test]
        public void Load_RejectsAnotherFormatHash()
        {
            var bytes = SavedBytecode();
            bytes[5] ^= 1;
            var e = Assert.Throws<InvalidDataException>(() => Load(bytes));
            StringAssert.Contains("hash", e.Message);
        }

        [Test]
        public void Load_RejectsATruncatedHeader()
        {
            var bytes = SavedBytecode();
            foreach (var length in new[] { 0, 3, 4, 5, 8 })
                Assert.Throws<InvalidDataException>(() => Load(bytes.Take(length).ToArray()), length.ToString());
        }

        // ── Compiler output against the table (A.19, DX-5) ──────────────

        [Test]
        public void Disassembler_DecodesHandlerAndIndexSetOpcodes()
        {
            var script = new WarScriptLanguage("t", @"
fun f [arr, i]
    begin
        arr{i} = 1
    rescue e
        print e
    end
    return arr
end", null, null);
            var (_, definitions) = script.CompileWithoutRunning();
            var chunk = definitions.GetFunction("f", 2).Compiled.Chunk;
            var text = chunk.Disassemble("f");
            StringAssert.Contains("PushHandler", text);
            StringAssert.Contains("IndexSetLocal", text);
            Assert.IsNull(CheckAgainstTable("f", chunk));
            Assert.AreEqual(chunk.InstructionStarts().Count, text.Split('\n').Length - 2,
                "one disassembly line per instruction");
        }

        [Test]
        public void Disassembler_DoesNotThrowOnMalformedCode()
        {
            var chunk = new Chunk();
            chunk.Code.AddRange(new byte[] { 58, (byte)OpCode.Constant, 0x12, 0x34, (byte)OpCode.PushHandler, 0 });
            string text = null;
            Assert.DoesNotThrow(() => text = chunk.Disassemble("bad"));
            StringAssert.Contains("<invalid opcode 58>", text);
            StringAssert.Contains("(out of range)", text);
            StringAssert.Contains("PushHandler <truncated>", text);
        }

        public static IEnumerable<TestCaseData> GoldenSources()
        {
            var root = GoldenRoot();
            if (!Directory.Exists(root)) yield break;
            foreach (var path in Directory.GetFiles(root, "*.ws", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                var relative = path.Substring(root.Length + 1).Replace('\\', '/');
                yield return new TestCaseData(relative).SetName("CompiledGolden(" + relative + ")");
            }
        }

        /// <summary>
        /// Every function the compiler produces for a golden file decodes
        /// through <see cref="OpInfo"/>: known opcodes, instructions that end
        /// exactly at the end of the code, jump and handler targets on
        /// instruction boundaries, constant indices in range, name operands
        /// that are Text, one line per code byte. Files that do not compile
        /// are skipped (their goldens check the compile error).
        /// </summary>
        [TestCaseSource(nameof(GoldenSources))]
        public void CompiledGolden_MatchesTheOpcodeTable(string relativePath)
        {
            var source = File.ReadAllText(Path.Combine(GoldenRoot(), relativePath));
            CompiledFunction body;
            DefinitionScope definitions;
            try
            {
                (body, definitions) = new WarScriptLanguage("t", source, null, null).CompileWithoutRunning();
            }
            catch (System.Exception e)
            {
                Assert.Pass("does not compile: " + e.GetType().Name);
                return;
            }

            var problems = new List<string>();
            foreach (var (name, function) in AllFunctions(body, definitions))
            {
                var problem = CheckAgainstTable(name, function.Chunk);
                if (problem != null) problems.Add(problem);
                Assert.DoesNotThrow(() => function.Chunk.Disassemble(name), name);
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static IEnumerable<(string, CompiledFunction)> AllFunctions(CompiledFunction body, DefinitionScope scope)
        {
            var result = new List<(string, CompiledFunction)>();
            void AddFunction(string name, CompiledFunction f)
            {
                if (f == null) return;
                result.Add((name, f));
                foreach (var c in f.Chunk.Constants)
                    if (c.Tag == ValueTag.NativeObject && c.Ref is CompiledFunction lambda)
                        AddFunction(name + "/" + lambda.Name, lambda);
            }
            void AddScope(string prefix, DefinitionScope s)
            {
                foreach (var f in s.Functions)
                    if (!(f is NativeFunctionDefinition)) AddFunction(prefix + f.Details.Name, f.Compiled);
                foreach (var c in s.ClassDefinitions)
                {
                    AddFunction(prefix + c.ClassDetails.Name + "#ctor", c.CompiledConstructor);
                    AddScope(prefix + c.ClassDetails.Name + ".", c.GetDefinitionScope());
                }
            }
            AddFunction("<script>", body);
            AddScope("", scope);
            return result;
        }

        /// <summary>Null when <paramref name="chunk"/> decodes cleanly through the table.</summary>
        private static string CheckAgainstTable(string name, Chunk chunk)
        {
            var code = chunk.Code;
            if (chunk.Lines.Count != code.Count) return $"{name}: {chunk.Lines.Count} lines for {code.Count} code bytes";
            var starts = new HashSet<int>(chunk.InstructionStarts());
            bool IsTarget(int t) => t == code.Count || starts.Contains(t);
            int at = 0;
            while (at < code.Count)
            {
                var info = OpInfo.Get(code[at]);
                if (info == null) return $"{name}@{at}: unknown opcode {code[at]}";
                if (at + info.Length > code.Count) return $"{name}@{at}: {info.Name} runs past the end";
                int end = at + info.Length, p = at + 1;
                foreach (var kind in info.Operands)
                {
                    int v = OpInfo.OperandSize(kind) == 1 ? code[p] : (code[p] << 8) | code[p + 1];
                    p += OpInfo.OperandSize(kind);
                    switch (kind)
                    {
                        case OperandKind.U16Const when v >= chunk.Constants.Count:
                        case OperandKind.U16Name when v >= chunk.Constants.Count:
                            return $"{name}@{at}: {info.Name} constant {v} out of range";
                        case OperandKind.U16Name when chunk.Constants[v].Tag != ValueTag.Text:
                            return $"{name}@{at}: {info.Name} name operand is {chunk.Constants[v].Tag}";
                        case OperandKind.U16JumpForward when !IsTarget(end + v):
                        case OperandKind.U16JumpBackward when !IsTarget(end - v):
                        case OperandKind.U16Address when !IsTarget(v):
                            return $"{name}@{at}: {info.Name} target is not an instruction boundary";
                        case OperandKind.U8Flag when v > 1:
                            return $"{name}@{at}: {info.Name} flag {v}";
                    }
                }
                at = end;
            }
            return null;
        }

        private static string GoldenRoot([CallerFilePath] string sourceFilePath = "") =>
            Path.Combine(Path.GetDirectoryName(sourceFilePath), "Golden~");
    }
}
