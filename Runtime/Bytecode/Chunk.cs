#nullable enable

using System.Collections.Generic;
using System.Text;
using WarScript.Expression.Value;

namespace WarScript.Bytecode
{
    /// <summary>
    /// A sequence of bytecode instructions with an associated constant pool.
    /// Each CompiledFunction owns one Chunk.
    /// </summary>
    public class Chunk
    {
        public readonly List<byte> Code = new();
        public readonly List<WarValue> Constants = new();
        public readonly List<int> Lines = new();

        /// <summary>
        /// Inline caches for property access. Each GetProperty/SetProperty/IndexSetProp
        /// bytecode site gets a cache slot ID (emitted as a U16 operand). At runtime,
        /// the VM checks if the instance's ClassDetails matches the cached type. On hit:
        /// one reference comparison + one array index — no string hashing.
        /// </summary>
        internal InlineCache[] PropertyCaches = System.Array.Empty<InlineCache>();
        private int _nextCacheSlot;

        public int AllocCacheSlot()
        {
            return _nextCacheSlot++;
        }

        public void FinalizePropertyCaches()
        {
            PropertyCaches = new InlineCache[_nextCacheSlot];
        }

        public int Count => Code.Count;

        /// <summary>
        /// Byte offset of the last opcode emitted (not operand bytes).
        /// Used by the peephole optimizer to safely identify the last instruction.
        /// </summary>
        internal int LastOpOffset { get; private set; } = -1;

        // ── Constant pool ──

        public int AddConstant(in WarValue value)
        {
            for (int i = 0; i < Constants.Count; i++)
            {
                if (Constants[i].Tag == value.Tag)
                {
                    if (value.IsNumeric && Constants[i].Numeric == value.Numeric) return i;
                    if (value.IsText && Constants[i].TextValue == value.TextValue) return i;
                }
            }
            Constants.Add(value);
            return Constants.Count - 1;
        }

        // ── Emit helpers ──

        public void EmitOp(OpCode op, int line)
        {
            LastOpOffset = Code.Count;
            Code.Add((byte)op);
            Lines.Add(line);
        }

        public void EmitByte(byte b, int line)
        {
            Code.Add(b);
            Lines.Add(line);
        }

        public void EmitU16(int value, int line)
        {
            Code.Add((byte)((value >> 8) & 0xFF));
            Lines.Add(line);
            Code.Add((byte)(value & 0xFF));
            Lines.Add(line);
        }

        public int EmitConstant(in WarValue value, int line)
        {
            var idx = AddConstant(value);
            EmitOp(OpCode.Constant, line);
            EmitU16(idx, line);
            return idx;
        }

        public int EmitJump(OpCode op, int line)
        {
            EmitOp(op, line);
            EmitByte(0xFF, line);
            EmitByte(0xFF, line);
            return Code.Count - 2;
        }

        public void PatchJump(int offset)
        {
            var jump = Code.Count - offset - 2;
            Code[offset]     = (byte)((jump >> 8) & 0xFF);
            Code[offset + 1] = (byte)(jump & 0xFF);
        }

        public void EmitLoop(int loopStart, int line)
        {
            EmitOp(OpCode.Loop, line);
            var offset = Code.Count - loopStart + 2;
            EmitU16(offset, line);
        }

        // ── Disassembly (debug) ──

        public string Disassemble(string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {name} ===");
            int i = 0;
            while (i < Code.Count)
                i = DisassembleInstruction(sb, i);
            return sb.ToString();
        }

        private int DisassembleInstruction(StringBuilder sb, int offset)
        {
            sb.Append($"{offset:D4} ");
            if (offset > 0 && offset < Lines.Count && Lines[offset] == Lines[offset - 1])
                sb.Append("   | ");
            else if (offset < Lines.Count)
                sb.Append($"{Lines[offset],4} ");
            else
                sb.Append("   ? ");

            var op = (OpCode)Code[offset];
            switch (op)
            {
                case OpCode.Constant:
                    var ci = ReadU16(offset + 1);
                    sb.AppendLine($"CONSTANT        {ci} ({Constants[ci]})");
                    return offset + 3;
                case OpCode.GetLocal: case OpCode.SetLocal:
                    sb.AppendLine($"{op,-16}{ReadU16(offset + 1)}");
                    return offset + 3;
                case OpCode.GetGlobal: case OpCode.SetGlobal:
                    var gi = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{gi} ({Constants[gi]})");
                    return offset + 3;
                case OpCode.Jump: case OpCode.JumpIfFalse: case OpCode.JumpIfTrue:
                    var fwd = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{offset} -> {offset + 3 + fwd}");
                    return offset + 3;
                case OpCode.Loop:
                    var bck = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{offset} -> {offset + 3 - bck}");
                    return offset + 3;
                case OpCode.Call: case OpCode.TailCall: case OpCode.CallMethod:
                    var ni = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{Constants[ni]} ({Code[offset + 3]} args)");
                    return offset + 4;
                case OpCode.CallValue:
                    sb.AppendLine($"CALL_VALUE      ({Code[offset + 1]} args)");
                    return offset + 2;
                case OpCode.NewArray:
                    sb.AppendLine($"NEW_ARRAY       {ReadU16(offset + 1)}");
                    return offset + 3;
                case OpCode.NewInstance:
                    var ii = ReadU16(offset + 1);
                    sb.AppendLine($"NEW_INSTANCE    {Constants[ii]} ({Code[offset + 3]} args)");
                    return offset + 4;
                case OpCode.GetProperty: case OpCode.SetProperty:
                case OpCode.IndexSetProp:
                case OpCode.ThisGetProperty: case OpCode.ThisSetProperty:
                    var pi = ReadU16(offset + 1);
                    var cs = ReadU16(offset + 3);
                    sb.AppendLine($"{op,-16}{Constants[pi]} (cache={cs})");
                    return offset + 5;
                case OpCode.LessJump: case OpCode.LessEqualJump:
                case OpCode.GreaterJump: case OpCode.GreaterEqualJump:
                case OpCode.EqualJump: case OpCode.NotEqualJump:
                    var cjFwd = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{offset} -> {offset + 3 + cjFwd}");
                    return offset + 3;
                case OpCode.CastAs: case OpCode.InstanceOf:
                    var ci2 = ReadU16(offset + 1);
                    sb.AppendLine($"{op,-16}{Constants[ci2]}");
                    return offset + 3;
                case OpCode.PopN:
                    sb.AppendLine($"POP_N           {Code[offset + 1]}");
                    return offset + 2;
                case OpCode.Import:
                    var mi = ReadU16(offset + 1);
                    sb.AppendLine($"IMPORT          {Constants[mi]}");
                    return offset + 3;
                case OpCode.PushHandler:
                    sb.AppendLine($"PUSH_HANDLER    rescue={ReadU16(offset+1)} ensure={ReadU16(offset+3)} end={ReadU16(offset+5)}");
                    return offset + 7;
                default:
                    sb.AppendLine($"{op}");
                    return offset + 1;
            }
        }

        private int ReadU16(int offset)
        {
            return (Code[offset] << 8) | Code[offset + 1];
        }
    }

    /// <summary>
    /// Per-bytecode-site cache for property access. Stores the last-seen class
    /// type and its property index. On cache hit (same ClassDetails reference),
    /// the VM uses the index directly — O(1) array access, no dictionary lookup.
    /// </summary>
    internal struct InlineCache
    {
        /// <summary>The ClassDetails of the last instance accessed at this site.</summary>
        public WarScript.Context.Definition.ClassDetails? CachedType;

        /// <summary>The property index within that class's PropertyValues array.</summary>
        public int CachedIndex;
    }
}
