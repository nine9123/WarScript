#nullable enable

using System.Collections.Generic;
using System.Globalization;
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
        /// The script the code was compiled from (the instance's script name or
        /// an import path), for audit events (plan 8.1). Not serialized: null
        /// for bytecode loaded with LoadBytecode or a BytecodeResolver.
        /// </summary>
        internal string? Source;

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

        // Dedup indices for the constant pool, keyed on the value's underlying
        // representation (fixed-point raw bits for numerics, the ordinal string for
        // text).
        private Dictionary<long, int>? _numericConstantIndex;
        private Dictionary<string, int>? _textConstantIndex;

        public int AddConstant(in WarValue value)
        {
            // Deduplicate identical constants so repeated literals and identifiers
            // (e.g. a function name emitted at every call site) share one pool slot.
            // A hash index makes this O(1) amortized.
            //
            // Only Numeric and Text are deduplicated.
            switch (value.Tag)
            {
                case ValueTag.Numeric:
                {
                    var key = value.Numeric.Raw;
                    _numericConstantIndex ??= new Dictionary<long, int>();
                    if (_numericConstantIndex.TryGetValue(key, out var existing))
                        return existing;
                    var idx = Constants.Count;
                    Constants.Add(value);
                    _numericConstantIndex[key] = idx;
                    return idx;
                }
                case ValueTag.Text:
                {
                    var key = value.TextValue;
                    _textConstantIndex ??= new Dictionary<string, int>(System.StringComparer.Ordinal);
                    if (_textConstantIndex.TryGetValue(key, out var existing))
                        return existing;
                    var idx = Constants.Count;
                    Constants.Add(value);
                    _textConstantIndex[key] = idx;
                    return idx;
                }
                default:
                    Constants.Add(value);
                    return Constants.Count - 1;
            }
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
            sb.AppendLine("=== " + name + " ===");
            int i = 0;
            while (i < Code.Count)
                i = DisassembleInstruction(sb, i);
            return sb.ToString();
        }

        /// <summary>
        /// The offsets at which <see cref="Disassemble"/> starts an
        /// instruction: each instruction's length comes from
        /// <see cref="OpInfo"/>; an unknown opcode byte counts as one byte.
        /// </summary>
        internal List<int> InstructionStarts()
        {
            var starts = new List<int>();
            int i = 0;
            while (i < Code.Count)
            {
                starts.Add(i);
                i += OpInfo.Get(Code[i])?.Length ?? 1;
            }
            return starts;
        }

        /// <summary>
        /// One instruction, decoded through <see cref="OpInfo"/> (T1.7,
        /// DX-5). Never throws on malformed code: an unknown opcode, an
        /// instruction cut off by the end of the code or a constant index
        /// out of range is written as such.
        /// </summary>
        private int DisassembleInstruction(StringBuilder sb, int offset)
        {
            sb.Append(offset.ToString("D4", CultureInfo.InvariantCulture)).Append(' ');
            if (offset > 0 && offset < Lines.Count && Lines[offset] == Lines[offset - 1])
                sb.Append("   | ");
            else if (offset < Lines.Count)
                sb.Append(Lines[offset].ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append(' ');
            else
                sb.Append("   ? ");

            var info = OpInfo.Get(Code[offset]);
            if (info == null)
            {
                sb.Append("<invalid opcode ").Append(Code[offset].ToString(CultureInfo.InvariantCulture)).AppendLine(">");
                return offset + 1;
            }
            if (offset + info.Length > Code.Count)
            {
                sb.Append(info.Name).AppendLine(" <truncated>");
                return Code.Count;
            }

            sb.Append(info.Name.PadRight(16));
            int at = offset + 1;
            int end = offset + info.Length;
            for (int k = 0; k < info.Operands.Length; k++)
            {
                var kind = info.Operands[k];
                int value = OpInfo.OperandSize(kind) == 1 ? Code[at] : ReadU16(at);
                at += OpInfo.OperandSize(kind);
                if (k > 0) sb.Append(' ');
                switch (kind)
                {
                    case OperandKind.U16Const:
                    case OperandKind.U16Name:
                        sb.Append(value.ToString(CultureInfo.InvariantCulture));
                        if (value < Constants.Count) sb.Append(" (").Append(Constants[value].Format(null)).Append(')');
                        else sb.Append(" (out of range)");
                        break;
                    case OperandKind.U16JumpForward:
                        sb.Append("-> ").Append((end + value).ToString(CultureInfo.InvariantCulture));
                        break;
                    case OperandKind.U16JumpBackward:
                        sb.Append("-> ").Append((end - value).ToString(CultureInfo.InvariantCulture));
                        break;
                    case OperandKind.U16Address:
                        sb.Append('@').Append(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    case OperandKind.U16Local:
                        sb.Append("slot ").Append(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    case OperandKind.U16Cache:
                        sb.Append("cache=").Append(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    case OperandKind.U8Count:
                    case OperandKind.U16Count:
                        sb.Append('#').Append(value.ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        sb.Append(value.ToString(CultureInfo.InvariantCulture));
                        break;
                }
            }
            sb.AppendLine();
            return end;
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
