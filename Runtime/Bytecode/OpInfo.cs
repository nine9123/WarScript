#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace WarScript.Bytecode
{
    /// <summary>
    /// What an instruction operand is (v2 plan 10.1). Every operand is one
    /// byte (<c>U8*</c>) or two bytes, high byte first (<c>U16*</c>).
    /// </summary>
    internal enum OperandKind : byte
    {
        U8Count,          // argument count (byte)
        U8Flag,           // 0 or 1
        U16Const,         // constant index, any tag
        U16Name,          // constant index that must refer to Text
        U16Local,         // local slot
        U16JumpForward,   // added to IP after the operands
        U16JumpBackward,  // subtracted from IP after the operands
        U16Cache,         // inline-cache slot
        U16Address,       // absolute code offset (exception-handler targets)
        U16Count          // element count (NewArray)
    }

    /// <summary>
    /// One opcode's operands and stack effect (v2 plan 10.1, T1.7). The
    /// single table <see cref="OpInfo.Table"/> drives the disassembler and
    /// the format hash, and is what the verifier will check code against.
    /// </summary>
    internal sealed class OpInfo
    {
        /// <summary>Marks <see cref="Pops"/> as computed by <see cref="StackEffect"/>.</summary>
        public const int Variable = -1;

        public readonly OpCode Op;
        public readonly string Name;
        public readonly OperandKind[] Operands;

        /// <summary>
        /// Values the instruction needs on the stack and takes off it, or
        /// <see cref="Variable"/> (then see <see cref="StackEffect"/>). An
        /// instruction that only looks at the top value (<c>SetLocal</c>,
        /// <c>JumpIfFalse</c>) pops 1 and pushes 1.
        /// </summary>
        public readonly int Pops;

        /// <summary>Values the instruction leaves on the stack.</summary>
        public readonly int Pushes;

        /// <summary><c>Return</c>, <c>TailCall</c>, <c>Raise</c>: no fall-through.</summary>
        public readonly bool Terminates;

        /// <summary><c>Jump</c>, <c>Loop</c>: no fall-through.</summary>
        public readonly bool UnconditionalJump;

        /// <summary>The instruction's length in bytes, opcode included.</summary>
        public readonly int Length;

        private OpInfo(OpCode op, int pops, int pushes, bool terminates, bool unconditionalJump,
            OperandKind[] operands)
        {
            Op = op;
            Name = Enum.GetName(typeof(OpCode), op)!;
            Operands = operands;
            Pops = pops;
            Pushes = pushes;
            Terminates = terminates;
            UnconditionalJump = unconditionalJump;
            int length = 1;
            foreach (var k in operands) length += OperandSize(k);
            Length = length;
        }

        public static int OperandSize(OperandKind kind) =>
            kind == OperandKind.U8Count || kind == OperandKind.U8Flag ? 1 : 2;

        private static OpInfo Op_(OpCode op, int pops, int pushes, params OperandKind[] operands) =>
            new OpInfo(op, pops, pushes, false, false, operands);

        private static OpInfo End(OpCode op, int pops, params OperandKind[] operands) =>
            new OpInfo(op, pops, 0, true, false, operands);

        private static OpInfo Goto(OpCode op, OperandKind operand) =>
            new OpInfo(op, 0, 0, false, true, new[] { operand });

        private const OperandKind Count8 = OperandKind.U8Count;
        private const OperandKind Flag8 = OperandKind.U8Flag;
        private const OperandKind Const = OperandKind.U16Const;
        private const OperandKind Name16 = OperandKind.U16Name;
        private const OperandKind Local = OperandKind.U16Local;
        private const OperandKind Fwd = OperandKind.U16JumpForward;
        private const OperandKind Back = OperandKind.U16JumpBackward;
        private const OperandKind Cache = OperandKind.U16Cache;
        private const OperandKind Addr = OperandKind.U16Address;
        private const OperandKind Count16 = OperandKind.U16Count;
        private const int Var = Variable;

        /// <summary>
        /// Every opcode, in increasing <see cref="OpCode"/> value. Changing an
        /// entry changes <see cref="BytecodeFormat.ComputeHash"/>: bump
        /// <see cref="BytecodeFormat.Version"/> and record the new
        /// <see cref="BytecodeFormat.Hash"/> (a test checks both).
        /// </summary>
        public static readonly IReadOnlyList<OpInfo> Table = new[]
        {
            Op_(OpCode.Constant, 0, 1, Const),
            Op_(OpCode.Null, 0, 1),
            Op_(OpCode.True, 0, 1),
            Op_(OpCode.False, 0, 1),

            Op_(OpCode.Pop, 1, 0),
            Op_(OpCode.PopN, Var, 0, Count8),
            Op_(OpCode.Dup, 1, 2),

            Op_(OpCode.PushScope, 0, 0),
            Op_(OpCode.PopScope, 0, 0),

            Op_(OpCode.GetLocal, 0, 1, Local),
            Op_(OpCode.SetLocal, 1, 1, Local),
            Op_(OpCode.GetGlobal, 0, 1, Name16),
            Op_(OpCode.SetGlobal, 1, 1, Name16),

            Op_(OpCode.Add, 2, 1),
            Op_(OpCode.Sub, 2, 1),
            Op_(OpCode.Mul, 2, 1),
            Op_(OpCode.Div, 2, 1),
            Op_(OpCode.Mod, 2, 1),
            Op_(OpCode.Negate, 1, 1),

            Op_(OpCode.Equal, 2, 1),
            Op_(OpCode.NotEqual, 2, 1),
            Op_(OpCode.Less, 2, 1),
            Op_(OpCode.LessEqual, 2, 1),
            Op_(OpCode.Greater, 2, 1),
            Op_(OpCode.GreaterEqual, 2, 1),

            Op_(OpCode.Not, 1, 1),

            Goto(OpCode.Jump, Fwd),
            Op_(OpCode.JumpIfFalse, 1, 1, Fwd),
            Op_(OpCode.JumpIfTrue, 1, 1, Fwd),
            Goto(OpCode.Loop, Back),

            Op_(OpCode.Call, Var, 1, Name16, Count8),
            End(OpCode.TailCall, Var, Name16, Count8),
            Op_(OpCode.CallValue, Var, 1, Count8),
            End(OpCode.Return, 1),

            Op_(OpCode.NewArray, Var, 1, Count16),
            Op_(OpCode.IndexGet, 2, 1),
            Op_(OpCode.IndexSet, 3, 1),
            Op_(OpCode.IndexSetLocal, 2, 1, Local),
            Op_(OpCode.IndexSetGlobal, 2, 1, Name16),
            Op_(OpCode.IndexSetProp, 3, 1, Name16, Cache),
            Op_(OpCode.ArrayAppend, 2, 1),

            Op_(OpCode.Len, 1, 1),
            Op_(OpCode.IterPrepare, 1, 1),

            Op_(OpCode.NewInstance, Var, 1, Name16, Count8),
            Op_(OpCode.NewNestedInstance, Var, 1, Name16, Count8),
            Op_(OpCode.GetProperty, 1, 1, Name16, Cache),
            Op_(OpCode.SetProperty, 2, 1, Name16, Cache),
            Op_(OpCode.CallMethod, Var, 1, Name16, Count8),
            Op_(OpCode.This, 0, 1),
            Op_(OpCode.CastAs, 1, 1, Name16),
            Op_(OpCode.InstanceOf, 1, 1, Name16),

            Op_(OpCode.Print, 1, 0),
            Op_(OpCode.Assert, 1, 0),

            Op_(OpCode.PushHandler, 0, 0, Addr, Addr, Addr, Flag8),
            Op_(OpCode.PopHandler, 0, 0),
            End(OpCode.Raise, 1),

            Op_(OpCode.Yield, 0, 0),
            Op_(OpCode.YieldWait, 1, 0),

            Op_(OpCode.Import, 0, 0, Name16),

            Op_(OpCode.LessJump, 2, 0, Fwd),

            Op_(OpCode.ThisGetProperty, 0, 1, Name16, Cache),
            Op_(OpCode.ThisSetProperty, 1, 1, Name16, Cache),

            Op_(OpCode.DropPending, 0, 0, Addr),
        };

        private static readonly OpInfo?[] ByOpcode = BuildIndex();

        private static OpInfo?[] BuildIndex()
        {
            var index = new OpInfo?[256];
            foreach (var info in Table) index[(byte)info.Op] = info;
            return index;
        }

        /// <summary>The entry for an opcode byte, or null when the byte is no opcode.</summary>
        public static OpInfo? Get(byte opcode) => ByOpcode[opcode];

        /// <summary>
        /// Values the instruction at <paramref name="offset"/> pops (10.1):
        /// <see cref="Pops"/>, or for a variable effect the count operand
        /// (plus 1 for the callee, receiver or parent under the arguments).
        /// The operands must lie inside <paramref name="code"/>.
        /// </summary>
        public int PopsAt(IReadOnlyList<byte> code, int offset)
        {
            if (Pops != Variable) return Pops;
            switch (Op)
            {
                case OpCode.Call:
                case OpCode.TailCall:
                case OpCode.NewInstance:
                    return code[offset + 3];
                case OpCode.CallMethod:
                case OpCode.NewNestedInstance:
                    return code[offset + 3] + 1;
                case OpCode.CallValue:
                    return code[offset + 1] + 1;
                case OpCode.PopN:
                    return code[offset + 1];
                case OpCode.NewArray:
                    return (code[offset + 1] << 8) | code[offset + 2];
                default:
                    throw new InvalidOperationException("no variable stack effect for " + Name);
            }
        }
    }

    /// <summary>
    /// The bytecode file format's identity (v2 plan 10.2, PROP-18): the
    /// loader accepts exactly this <see cref="Version"/> and
    /// <see cref="Hash"/>.
    /// </summary>
    internal static class BytecodeFormat
    {
        /// <summary>
        /// Bumped by every change to the opcode table, an operand layout or
        /// the file layout (version 1 was the v1 format, whose opcode values
        /// were implicit; version 2 stored one line per code byte; version 3
        /// compiled a rescue body without the PopHandler that version 4 ends
        /// it with when the block has an ensure block, Q-14).
        /// </summary>
        public const byte Version = 4;

        /// <summary>
        /// Names the file layout for the hash. Change it (and the version)
        /// with any change to what <see cref="BytecodeSerializer"/> writes.
        /// </summary>
        public const string Layout =
            "WSBC magic|version u8|hash u32; v1 function, constant and definition records; line table u32 count of (u16 offset, i32 line) where the line changes; a rescue body of a block with an ensure block runs under an ensure-only handler and ends with PopHandler";

        /// <summary>
        /// <see cref="ComputeHash"/> for the current table, version and
        /// layout. <c>BytecodeFormatTests</c> recomputes it.
        /// </summary>
        public const uint Hash = 0x156C8028;

        /// <summary>
        /// FNV-1a (32-bit) over, for each <see cref="OpInfo"/> entry: its
        /// opcode value, its name, its operand kinds, its pops, pushes and
        /// flags; then <see cref="Version"/> and <see cref="Layout"/>.
        /// Strings are a u16 length and their UTF-8 bytes, integers little-endian.
        /// </summary>
        public static uint ComputeHash()
        {
            uint h = 2166136261;
            void Byte(int b) { h ^= (byte)b; h *= 16777619; }
            void Int(int v) { Byte(v); Byte(v >> 8); Byte(v >> 16); Byte(v >> 24); }
            void Str(string s)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                Byte(bytes.Length); Byte(bytes.Length >> 8);
                foreach (var b in bytes) Byte(b);
            }

            foreach (var info in OpInfo.Table)
            {
                Byte((byte)info.Op);
                Str(info.Name);
                Byte(info.Operands.Length);
                foreach (var k in info.Operands) Byte((byte)k);
                Int(info.Pops);
                Int(info.Pushes);
                Byte((info.Terminates ? 1 : 0) | (info.UnconditionalJump ? 2 : 0));
            }
            Byte(Version);
            Str(Layout);
            return h;
        }
    }
}
