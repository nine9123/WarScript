#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WarScript.Exception;
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
        /// <summary>The source line and column of every code byte, run-length encoded (T1.7, 10.2, T3.1).</summary>
        public readonly LineTable Lines = new();

        /// <summary>
        /// The column marked with every byte emitted from now on (T3.1), 0
        /// when unknown: the compiler keeps it at the statement or
        /// expression being compiled. A byte emitted with line 0 gets
        /// column 0. Not serialized (the file's line table has no columns
        /// until format v3, T3.11).
        /// </summary>
        internal int Column;

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
        /// At most this many constants per function (T1.8): adding the
        /// 65,536th raises a <see cref="CompileError"/>.
        /// </summary>
        public const int MaxConstants = 65535;

        /// <summary>The message for code, a jump or an address past 16 bits (T1.8).</summary>
        internal const string FunctionTooLarge = "function too large (bytecode exceeds 65,535 bytes)";

        /// <summary>
        /// A <see cref="CompileError"/> for this chunk's script. The line is
        /// left 0: the compiler fills in the statement being compiled.
        /// </summary>
        internal CompileError LimitError(string reason) => new CompileError(reason, Source, 0);

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
            // Raises before a new slot is added; a constant already in the
            // pool is still found below.
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
                    CheckConstantRoom();
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
                    CheckConstantRoom();
                    var idx = Constants.Count;
                    Constants.Add(value);
                    _textConstantIndex[key] = idx;
                    return idx;
                }
                default:
                    CheckConstantRoom();
                    Constants.Add(value);
                    return Constants.Count - 1;
            }
        }

        private void CheckConstantRoom()
        {
            if (Constants.Count >= MaxConstants)
                throw LimitError("too many constants in one function (more than 65,535)");
        }

        // ── Emit helpers ──

        public void EmitOp(OpCode op, int line)
        {
            LastOpOffset = Code.Count;
            Mark(line);
            Code.Add((byte)op);
        }

        public void EmitByte(byte b, int line)
        {
            Mark(line);
            Code.Add(b);
        }

        public void EmitU16(int value, int line)
        {
            if (value < 0 || value > 0xFFFF)
                throw LimitError("function too large (an operand exceeds 65,535)");
            Mark(line);
            Code.Add((byte)((value >> 8) & 0xFF));
            Code.Add((byte)(value & 0xFF));
        }

        private void Mark(int line) => Lines.Mark(Code.Count, line, line > 0 ? Column : 0);

        /// <summary>
        /// Pops <paramref name="count"/> values: one <c>Pop</c>, or <c>PopN</c>
        /// instructions of at most 255 each (T1.8; a <c>PopN</c> operand is
        /// one byte).
        /// </summary>
        public void EmitPops(int count, int line)
        {
            if (count == 1) { EmitOp(OpCode.Pop, line); return; }
            while (count > 0)
            {
                var n = count > 255 ? 255 : count;
                EmitOp(OpCode.PopN, line);
                EmitByte((byte)n, line);
                count -= n;
            }
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
            if (jump > 0xFFFF) throw LimitError(FunctionTooLarge);
            Code[offset]     = (byte)((jump >> 8) & 0xFF);
            Code[offset + 1] = (byte)(jump & 0xFF);
        }

        public void EmitLoop(int loopStart, int line)
        {
            EmitOp(OpCode.Loop, line);
            var offset = Code.Count - loopStart + 2;
            if (offset > 0xFFFF) throw LimitError(FunctionTooLarge);
            EmitU16(offset, line);
        }

        /// <summary>
        /// Writes the absolute code address <paramref name="address"/> into
        /// the U16 operand at <paramref name="at"/> (handler addresses).
        /// </summary>
        public void PatchAddress(int at, int address)
        {
            if (address > 0xFFFF) throw LimitError(FunctionTooLarge);
            Code[at]     = (byte)((address >> 8) & 0xFF);
            Code[at + 1] = (byte)(address & 0xFF);
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
            if (Lines.EntryCount == 0 || offset < Lines.OffsetAt(0))
                sb.Append("   ? ");
            else if (offset > Lines.OffsetAt(0) && Lines.GetLine(offset) == Lines.GetLine(offset - 1))
                sb.Append("   | ");
            else
                sb.Append(Lines.GetLine(offset).ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append(' ');

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
    /// The source line of each code byte, run-length encoded (v2 plan 10.2,
    /// T1.7, DX-3): one entry, a code offset and a line, for each point where
    /// the line changes, so a run of instructions on one line costs one
    /// entry instead of one <c>int</c> per byte. The entry covering an offset
    /// is the last one starting at or before it. A well-formed table
    /// (<see cref="Problem"/>) starts at offset 0 and its offsets increase
    /// strictly and stay below the code length.
    /// </summary>
    public sealed class LineTable
    {
        private readonly List<int> _offsets = new();
        private readonly List<int> _lines = new();
        private readonly List<int> _columns = new();

        /// <summary>The number of (offset, line, column) entries.</summary>
        public int EntryCount => _offsets.Count;

        /// <summary>The code offset where entry <paramref name="index"/> starts.</summary>
        public int OffsetAt(int index) => _offsets[index];

        /// <summary>The line of entry <paramref name="index"/>.</summary>
        public int LineAt(int index) => _lines[index];

        /// <summary>The column of entry <paramref name="index"/>, from 1; 0 when unknown (T3.1).</summary>
        public int ColumnAt(int index) => _columns[index];

        /// <summary>
        /// Records that the code byte at <paramref name="offset"/>, appended
        /// after every byte marked before it, is on <paramref name="line"/>
        /// at <paramref name="column"/> (T3.1; 0 = unknown): adds an entry
        /// only when the line or the column changes.
        /// </summary>
        public void Mark(int offset, int line, int column = 0)
        {
            int n = _lines.Count;
            if (n > 0 && _lines[n - 1] == line && _columns[n - 1] == column) return;
            _offsets.Add(offset);
            _lines.Add(line);
            _columns.Add(column);
        }

        /// <summary>
        /// Appends an entry as read from a file; <see cref="Problem"/> checks
        /// the result. The file has no columns before format v3 (T3.11).
        /// </summary>
        internal void AddEntry(int offset, int line)
        {
            _offsets.Add(offset);
            _lines.Add(line);
            _columns.Add(0);
        }

        /// <summary>
        /// The index of the entry covering <paramref name="offset"/>, or -1
        /// when none does (an empty table, or an offset before the first entry).
        /// </summary>
        public int EntryIndex(int offset)
        {
            int lo = 0, hi = _offsets.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_offsets[mid] <= offset) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        /// <summary>The line of the code byte at <paramref name="offset"/>, or 0 when no entry covers it.</summary>
        public int GetLine(int offset)
        {
            int index = EntryIndex(offset);
            return index < 0 ? 0 : _lines[index];
        }

        /// <summary>The column of the code byte at <paramref name="offset"/>, or 0 when unknown (T3.1).</summary>
        public int GetColumn(int offset)
        {
            int index = EntryIndex(offset);
            return index < 0 ? 0 : _columns[index];
        }

        /// <summary>
        /// Null when the table is well formed for code of
        /// <paramref name="codeLength"/> bytes: no entries for empty code,
        /// otherwise a first entry at offset 0 and strictly increasing
        /// offsets below the length. Otherwise what is wrong.
        /// </summary>
        public string? Problem(int codeLength)
        {
            int n = _offsets.Count;
            if (codeLength == 0) return n == 0 ? null : "line table has entries for empty code";
            if (n == 0 || _offsets[0] != 0) return "line table does not start at offset 0";
            for (int i = 1; i < n; i++)
                if (_offsets[i] <= _offsets[i - 1]) return "line table offsets do not increase";
            if (_offsets[n - 1] >= codeLength) return "line table runs past the code";
            return null;
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
