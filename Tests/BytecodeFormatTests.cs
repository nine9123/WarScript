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
using WarScript.Native;

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
            foreach (byte version in new byte[] { 1, 2, 4 })
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

        // ── Verifier and loader (10.3, 10.4; A.20, SEC-1) ───────────────

        private const byte Const = (byte)OpCode.Constant;
        private const byte Null_ = (byte)OpCode.Null;
        private const byte Ret = (byte)OpCode.Return;

        /// <summary>A function with the given code, all on line 1.</summary>
        private static CompiledFunction Function(string name, int arity, byte[] code,
            WarValue[] constants = null, int caches = 0)
        {
            var f = new CompiledFunction(name, arity);
            f.Chunk.Code.AddRange(code);
            if (code.Length > 0) f.Chunk.Lines.Mark(0, 1);
            if (constants != null) f.Chunk.Constants.AddRange(constants);
            f.Chunk.PropertyCaches = new InlineCache[caches];
            return f;
        }

        /// <summary>A bytecode file whose script body is <paramref name="body"/>, written by the real serializer.</summary>
        private static byte[] File_(CompiledFunction body)
        {
            var host = new WarScriptLanguage("t", "", null, null);
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BytecodeSerializer.Save(w, body, new DefinitionScope(host, null));
            return ms.ToArray();
        }

        private static InvalidDataException Rejected(byte[] bytes, string expected)
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("t", "", null, (w, m) => output.Add(m));
            var e = Assert.Throws<InvalidDataException>(() => script.LoadBytecode(new MemoryStream(bytes)));
            StringAssert.Contains(expected, e.Message);
            Assert.IsEmpty(output, "nothing ran");
            return e;
        }

        private static InvalidDataException RejectedBody(byte[] code, string expected, int arity = 0,
            WarValue[] constants = null, int caches = 0) =>
            Rejected(File_(Function("<script>", arity, code, constants, caches)), expected);

        [Test]
        public void Verifier_AcceptsAHandBuiltFunction_AndItRuns()
        {
            var output = new List<string>();
            var body = Function("<script>", 0, new byte[]
            {
                Const, 0, 0, (byte)OpCode.Print, Null_, Ret,
            }, new[] { WarValue.FromText("hi") });
            var script = new WarScriptLanguage("t", "", null, (w, m) => output.Add(m));
            script.LoadBytecode(new MemoryStream(File_(body)));
            script.Run();
            CollectionAssert.AreEqual(new[] { "hi" }, output);
        }

        [Test]
        public void Verifier_ComputesMaxStack()
        {
            // a + (1 + 2) with a in slot 0: heights 1, 2, 3, 4, 3, 2, then return.
            var f = Function("f", 1, new byte[]
            {
                (byte)OpCode.GetLocal, 0, 0, Const, 0, 0, Const, 0, 0,
                (byte)OpCode.Add, (byte)OpCode.Add, Ret,
            }, new[] { WarValue.FromNumeric(1) });
            BytecodeVerifier.Verify(f, atLoad: true);
            Assert.AreEqual(4, f.MaxStack);
        }

        // A.20, one test per case of the appendix.

        [Test]
        public void A20_TruncatedAfterTheHeader() =>
            Rejected(File_(Function("<script>", 0, new[] { Null_, Ret })).Take(9).ToArray(), "truncated bytecode");

        [Test]
        public void A20_TruncatedAnywhere_IsInvalidData()
        {
            var bytes = File_(Function("<script>", 0, new byte[] { Const, 0, 0, (byte)OpCode.Print, Null_, Ret },
                new[] { WarValue.FromText("hi") }));
            for (int length = 0; length < bytes.Length; length++)
                Assert.Throws<InvalidDataException>(() => Load(bytes.Take(length).ToArray()), length.ToString());
        }

        [Test]
        public void A20_JumpIntoTheMiddleOfAnInstruction() =>
            RejectedBody(new byte[] { (byte)OpCode.Jump, 0, 1, Const, 0, 0, Null_, Ret }, "not an instruction boundary",
                constants: new[] { WarValue.Null });

        [Test]
        public void A20_ConstantIndexOutOfRange() =>
            RejectedBody(new byte[] { Const, 0, 5, Ret }, "constant index out of range", constants: new[] { WarValue.Null });

        [Test]
        public void A20_NameOperandOnANumber() =>
            RejectedBody(new byte[] { (byte)OpCode.GetGlobal, 0, 0, Ret }, "not a text constant",
                constants: new[] { WarValue.FromNumeric(1) });

        [Test]
        public void A20_LocalSlotAtTheStackHeight()
        {
            RejectedBody(new byte[] { (byte)OpCode.GetLocal, 0, 0, Ret }, "slot is not below the stack height");
            // An argument is a live slot; the one above it is not.
            RejectedBody(new byte[] { (byte)OpCode.GetLocal, 0, 1, Ret }, "slot is not below the stack height", arity: 1);
        }

        [Test]
        public void A20_UnknownOpcode() =>
            RejectedBody(new byte[] { 58, Null_, Ret }, "unknown opcode 58");

        [Test]
        public void A20_LambdaConstantsNested1000Deep()
        {
            var inner = Function("<lambda>", 0, new[] { Null_, Ret });
            for (int i = 0; i < 1000; i++)
                inner = Function("<lambda>", 0, new[] { Null_, Ret }, new[] { WarValue.FromNativeObject(inner) });
            Rejected(File_(inner), "nest deeper than 32");
        }

        [Test]
        public void A20_PathFallsOffTheEnd() =>
            RejectedBody(new byte[] { Null_, (byte)OpCode.Pop }, "falls off the end");

        // Further rejections of 10.3 and 10.4.

        [Test]
        public void Verifier_RejectsStackUnderflow() =>
            RejectedBody(new byte[] { (byte)OpCode.Pop, Null_, Ret }, "needs 1 values, the stack has 0");

        [Test]
        public void Verifier_RejectsInconsistentHeightsAtAMerge() =>
            // True; JumpIfFalse +1 (over Null) → the target is reached at heights 1 and 2.
            RejectedBody(new byte[] { (byte)OpCode.True, (byte)OpCode.JumpIfFalse, 0, 1, Null_, Ret }, "differs from");

        [Test]
        public void Verifier_RejectsALoopThatJumpsForward() =>
            RejectedBody(new byte[] { (byte)OpCode.Loop, 0, 0, Null_, Ret }, "does not jump backward");

        [Test]
        public void Verifier_RejectsUnorderedHandlerAddresses()
        {
            // PushHandler rescue=8 ensure=0 end=8: ensure before the PushHandler.
            RejectedBody(new byte[] { (byte)OpCode.PushHandler, 0, 8, 0, 0, 0, 8, 1, Null_, Ret },
                "handler addresses are not ordered");
        }

        [Test]
        public void Verifier_RejectsACacheSlotOutOfRange() =>
            RejectedBody(new byte[] { (byte)OpCode.This, (byte)OpCode.GetProperty, 0, 0, 0, 0, Ret }, "cache slot out of range",
                constants: new[] { WarValue.FromText("x") });

        [Test]
        public void Verifier_RejectsMoreThanTheValueStack()
        {
            var code = Enumerable.Repeat(Null_, 1025).Concat(new[] { Ret }).ToArray();
            RejectedBody(code, "needs 1025 stack slots");
            var fits = Function("f", 0, Enumerable.Repeat(Null_, 1024).Concat(new[] { Ret }).ToArray());
            Assert.DoesNotThrow(() => BytecodeVerifier.Verify(fits, atLoad: true));
        }

        [Test]
        public void Verifier_RejectsPoppingAScopeOrHandlerTheFrameDidNotPush()
        {
            // Either would remove what the calling frame or the host pushed.
            RejectedBody(new byte[] { (byte)OpCode.PopScope, Null_, Ret }, "PopScope without an open block scope");
            RejectedBody(new byte[] { (byte)OpCode.PopHandler, Null_, Ret }, "PopHandler without an open handler");
        }

        [Test]
        public void Verifier_RejectsAMergeWithDifferentOpenScopes() =>
            // True; JumpIfFalse +1 (over PushScope); Pop: reached with 0 and 1 open scopes.
            RejectedBody(new byte[] { (byte)OpCode.True, (byte)OpCode.JumpIfFalse, 0, 1, (byte)OpCode.PushScope,
                (byte)OpCode.Pop, Null_, Ret }, "open scopes or handlers");

        [Test]
        public void Verifier_RejectsATailCallInsideAHandler()
        {
            // PushHandler rescue=9 ensure=10 end=10 has-rescue; TailCall f/0; rescue: Pop; ensure: Null, Return.
            RejectedBody(new byte[] { (byte)OpCode.PushHandler, 0, 13, 0, 14, 0, 14, 1, (byte)OpCode.TailCall, 0, 0, 0,
                Null_, (byte)OpCode.Pop, Null_, Ret }, "TailCall inside a block scope or handler",
                constants: new[] { WarValue.FromText("f") });
        }

        [Test]
        public void Verifier_RejectsAnArityAbove255AtLoad()
        {
            // A call's count operand is one byte: the VM could never pad up to it.
            RejectedBody(Enumerable.Repeat(Null_, 1).Concat(new[] { Ret }).ToArray(), "arity is above 255", arity: 256);
            Assert.DoesNotThrow(() => BytecodeVerifier.Verify(Function("f", 255, new[] { Null_, Ret }), atLoad: true));
        }

        [Test]
        public void Loader_RejectsAnArityOnTheScriptBodyOrAConstructor()
        {
            // The VM enters both frames with no arguments, so the verifier's
            // start height (the arity) would not hold: here Pop would run
            // below the frame's stack base.
            var underflow = new byte[] { (byte)OpCode.Pop, Null_, Ret };
            RejectedBody(underflow, "script body has arity 1", arity: 1);

            var host = new WarScriptLanguage("t", "class P []\n    x = 1\nend\np = new P []", null, null);
            var (body, definitions) = host.CompileWithoutRunning();
            definitions.ClassDefinitions.Single().CompiledConstructor = Function("P#ctor", 1, underflow);
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BytecodeSerializer.Save(w, body, definitions);
            Rejected(ms.ToArray(), "constructor of class 'P' has arity 1");
        }

        // ── Line table (10.2, DX-3) ─────────────────────────────────────

        [Test]
        public void LineTable_KeepsOneEntryPerChangeOfLine()
        {
            var lines = new LineTable();
            int[] perByte = { 3, 3, 3, 4, 4, 0, 0, 3 };
            for (int i = 0; i < perByte.Length; i++) lines.Mark(i, perByte[i]);
            Assert.AreEqual(4, lines.EntryCount);
            for (int i = 0; i < perByte.Length; i++) Assert.AreEqual(perByte[i], lines.GetLine(i), "offset " + i);
            Assert.AreEqual(0, new LineTable().GetLine(0));
            Assert.AreEqual(-1, lines.EntryIndex(-1));
            Assert.IsNull(lines.Problem(perByte.Length));
            StringAssert.Contains("runs past", lines.Problem(7));
            Assert.IsNull(new LineTable().Problem(0));
        }

        [Test]
        public void LineTable_RoundTripsThroughAFile_ForEveryByte()
        {
            var source = "x = 1\nfun f [a]\n    if a > 0\n        return a * 2\n    end\n    return 0\nend\n\nprint f [x]\nprint f [0]";
            var (body, definitions) = new WarScriptLanguage("t", source, null, null).CompileWithoutRunning();
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BytecodeSerializer.Save(w, body, definitions);
            ms.Position = 0;
            CompiledFunction loadedBody;
            DefinitionScope loaded;
            using (var r = new BinaryReader(ms))
                (loadedBody, loaded) = BytecodeSerializer.Load(r, new WarScriptLanguage("u", "", null, null), null);

            void Same(CompiledFunction a, CompiledFunction b)
            {
                Assert.AreEqual(a.Chunk.Lines.EntryCount, b.Chunk.Lines.EntryCount, a.Name);
                Assert.Less(a.Chunk.Lines.EntryCount, a.Chunk.Code.Count, a.Name + ": runs share entries");
                for (int i = 0; i < a.Chunk.Code.Count; i++)
                    Assert.AreEqual(a.Chunk.Lines.GetLine(i), b.Chunk.Lines.GetLine(i), a.Name + " offset " + i);
            }
            Same(body, loadedBody);
            Same(definitions.GetFunction("f", 1).Compiled, loaded.GetFunction("f", 1).Compiled);
            // The first return statement is on line 4 (expression opcodes may still be line 0, T1.10).
            var loadedF = loaded.GetFunction("f", 1).Compiled.Chunk;
            Assert.AreEqual(4, loadedF.Lines.GetLine(loadedF.Code.IndexOf((byte)OpCode.Return)));
        }

        private static CompiledFunction WithLines(byte[] code, params (int offset, int line)[] entries)
        {
            var f = new CompiledFunction("<script>", 0);
            f.Chunk.Code.AddRange(code);
            foreach (var (offset, line) in entries) f.Chunk.Lines.AddEntry(offset, line);
            return f;
        }

        [Test]
        public void Loader_RejectsAMalformedLineTable()
        {
            var code = new byte[] { Null_, (byte)OpCode.Pop, Null_, Ret };
            Rejected(File_(WithLines(code)), "line table does not start at offset 0");
            Rejected(File_(WithLines(code, (1, 1))), "line table does not start at offset 0");
            Rejected(File_(WithLines(code, (0, 1), (2, 2), (2, 3))), "line table offsets do not increase");
            Rejected(File_(WithLines(code, (0, 1), (4, 2))), "line table runs past the code");
            Rejected(File_(WithLines(code, (0, 1), (1, 2), (2, 3), (3, 4), (5, 5))), "more line entries than code bytes");
            Assert.DoesNotThrow(() => Load(File_(WithLines(code, (0, 1), (3, 2)))));
        }

        [Test]
        public void Loader_RejectsInvalidUtf8()
        {
            var bytes = File_(Function("<script>", 0, new[] { Null_, Ret }));
            // The script body's name follows the 9-byte header: u16 length, then "<script>".
            Assert.AreEqual((byte)'<', bytes[11]);
            bytes[11] = 0xFF;
            Rejected(bytes, "invalid UTF-8");
        }

        [Test]
        public void Loader_RejectsACountLargerThanTheBytesLeft()
        {
            var bytes = File_(Function("<script>", 0, new[] { Null_, Ret }));
            // name (2 + 8 bytes), arity, local count, then the constant count.
            int constCount = 9 + 10 + 2 + 2;
            bytes[constCount] = 0xFF;
            bytes[constCount + 1] = 0xFF;
            Rejected(bytes, "constant count");
        }

        [Test]
        public void Loader_RejectedFile_LeavesTheInstanceUnchanged()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage("t", "fun f []\n    print \"old\"\nend", null, (w, m) => output.Add(m));
            script.Run();
            var bad = File_(Function("<script>", 0, new byte[] { 58, Null_, Ret }));
            Assert.Throws<InvalidDataException>(() => script.LoadBytecode(new MemoryStream(bad)));
            script.Call(script.GetFunction("f", 0));
            CollectionAssert.AreEqual(new[] { "old" }, output);
        }

        [Test]
        public void Call_WithMoreArgumentsThanTheArity_CountsTheSurplus()
        {
            // The 200 extra arguments stay in the lambda's frame under its
            // 900-element literal: 1,100 slots, though its MaxStack alone fits.
            var literal = string.Join(", ", Enumerable.Range(0, 900));
            var args = string.Join(", ", Enumerable.Range(0, 200));
            var output = new List<string>();
            var script = new WarScriptLanguage("t",
                "g = fun [] return {" + literal + "} end\nbegin\n    g [" + args + "]\nrescue e\n    print e\nend\nprint \"after\"",
                null, (w, m) => output.Add(m));
            script.Run();
            Assert.IsNull(script.LastHostFault);
            CollectionAssert.AreEqual(new[] { "Value stack overflow", "after" }, output);
        }

        [Test]
        public void Call_OfAFunctionNeedingMoreThanTheStack_RaisesForTheCaller()
        {
            var literal = string.Join(", ", Enumerable.Range(0, 1100));
            var output = new List<string>();
            var script = new WarScriptLanguage("t",
                "fun big []\n    return {" + literal + "}\nend\nbegin\n    big []\nrescue e\n    print e\nend\nprint \"after\"",
                null, (w, m) => output.Add(m));
            script.Run();
            CollectionAssert.AreEqual(new[] { "Value stack overflow", "after" }, output);
        }

        /// <summary>
        /// A small, deterministic version of the fuzzer's bytecode mode (9.6):
        /// flip 1 to 8 bytes of a compiled golden, or truncate it; the result is
        /// either rejected with <see cref="InvalidDataException"/> or runs
        /// (budgeted) without a host fault.
        /// </summary>
        [TestCase("language/test_exceptions.ws")]
        [TestCase("language/test_classes_inheritance.ws")]
        [TestCase("language/break_out_of_begin.ws")]
        [TestCase("language/test_loops.ws")]
        [TestCase("language/test_nested_classes.ws")]
        [TestCase("language/test_functions.ws")]
        [TestCase("language/constructor_frames.ws")]
        [TestCase("language/ensure_leaving.ws")]
        public void MutatedBytecode_IsRejectedOrRunsWithoutHostFault(string relativePath)
        {
            var path = Path.Combine(GoldenRoot(), relativePath);
            var source = new WarScriptLanguage("t", File.ReadAllText(path), null, null);
            var ms = new MemoryStream();
            source.SaveBytecode(ms);
            var original = ms.ToArray();

            var random = new System.Random(relativePath.Length * 7919 + original.Length);
            int rejected = 0, ran = 0;
            for (int i = 0; i < 300; i++)
            {
                var bytes = (byte[])original.Clone();
                if (random.Next(8) == 0)
                    bytes = bytes.Take(random.Next(bytes.Length)).ToArray();
                else
                    for (int k = random.Next(1, 9); k > 0; k--)
                        bytes[9 + random.Next(bytes.Length - 9)] = (byte)random.Next(256); // keep the header

                var script = new WarScriptLanguage("t", "", null, (w, m) => { });
                WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
                script.InstructionBudget = 100_000;
                script.MemoryBudget = 16 * 1024 * 1024;
                try { script.LoadBytecode(new MemoryStream(bytes)); }
                catch (InvalidDataException) { rejected++; continue; }
                script.Run();
                Assert.IsNull(script.LastHostFault, "case " + i + ": " + script.LastHostFault);
                ran++;
            }
            Assert.Greater(rejected, 0);
            TestContext.WriteLine(relativePath + ": " + rejected + " rejected, " + ran + " ran");
        }

        /// <summary>
        /// The fuzzer's bytecode mode (9.6) runs each mutated file as a
        /// <c>.wsbc</c> through the runner: a file the loader refuses reports
        /// status "rejected", one it accepts runs.
        /// </summary>
        [Test]
        [Category("HeadlessOnly")]
        public void Runner_RunsABytecodeFile_OrReportsItRejected()
        {
            var runner = Tests.Golden.IsolatedRun.RunnerPath();
            if (runner == null) Assert.Ignore("The runner is part of the headless build only.");

            var dir = Path.Combine(Path.GetTempPath(), "warscript-wsbc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var good = Path.Combine(dir, "good.wsbc");
                File.WriteAllBytes(good, File_(Function("<script>", 0, new byte[] { Const, 0, 0, (byte)OpCode.Print, Null_, Ret },
                    new[] { WarValue.FromText("hi") })));
                var bad = Path.Combine(dir, "bad.wsbc");
                File.WriteAllBytes(bad, File_(Function("<script>", 0, new byte[] { (byte)OpCode.PopScope, Null_, Ret })));

                var ran = Tests.Golden.IsolatedRun.Execute(runner, Tests.Golden.GoldenFile.LoadBytecode(good),
                    Tests.Golden.GoldenMode.Source, "", 1000, 1024 * 1024);
                CollectionAssert.AreEqual(new[] { "hi" }, ran.Output);
                Assert.IsNull(ran.Rejected);
                Assert.IsNull(ran.EscapedException);

                var refused = Tests.Golden.IsolatedRun.Execute(runner, Tests.Golden.GoldenFile.LoadBytecode(bad),
                    Tests.Golden.GoldenMode.Source, "");
                StringAssert.Contains("PopScope without an open block scope", refused.Rejected);
                Assert.IsNull(refused.EscapedException);
                Assert.IsNull(refused.Hash, "nothing ran");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
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
        /// Every function the compiler produces for a golden file passes the
        /// verifier (with the load-time stack limit), its boundaries are the
        /// disassembler's, and it decodes through <see cref="OpInfo"/>: known opcodes, instructions that end
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
                Assert.DoesNotThrow(() => BytecodeVerifier.Verify(function, atLoad: true), name);
                Assert.That(function.MaxStack, Is.GreaterThanOrEqualTo(function.Arity), name);
                CollectionAssert.AreEqual(function.Chunk.InstructionStarts(), BytecodeVerifier.InstructionStarts(function), name);
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
            var lineProblem = chunk.Lines.Problem(code.Count);
            if (lineProblem != null) return name + ": " + lineProblem;
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
