#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FixMath;
using WarScript.Bytecode;
using WarScript.Exception;
using WarScript.Native;

namespace WarScript.Expression.Value
{
    public enum ValueTag : byte
    {
        Null = 0,
        Numeric,
        Logical,
        Text,
        Array,
        Class,
        NativeObject
    }

    /// <summary>
    /// Tagged union value type. Replaces the IValue class hierarchy.
    /// Numeric and Logical values are stored inline (no heap allocation).
    /// Reference types (Text, Array, Class, NativeObject) use the Ref field.
    /// </summary>
    public struct WarValue : IEquatable<WarValue>
    {
        public ValueTag Tag;

        /// <summary>
        /// Holds numeric value (Tag.Numeric) or encoded boolean
        /// (Tag.Logical: F64.One=true, F64.Zero=false).
        /// Deterministic 32.32 fixed-point — no IEEE floats anywhere in the VM.
        /// </summary>
        public F64 Numeric;

        /// <summary>
        /// Holds reference types:
        /// Tag.Text → string, Tag.Array → List&lt;WarValue&gt;,
        /// Tag.Class → ClassData, Tag.NativeObject → object
        /// </summary>
        public object? Ref;

        // ── Predicates ──

        public bool IsNull => Tag == ValueTag.Null;
        public bool IsNumeric => Tag == ValueTag.Numeric;
        public bool IsLogical => Tag == ValueTag.Logical;
        public bool IsText => Tag == ValueTag.Text;
        public bool IsArray => Tag == ValueTag.Array;
        public bool IsClass => Tag == ValueTag.Class;
        public bool IsNativeObject => Tag == ValueTag.NativeObject;

        // ── Typed accessors ──

        public F64 NumericValue => Numeric;
        public bool LogicalValue => Numeric != F64.Zero;
        public string TextValue => (string)Ref!;
        public List<WarValue> ArrayValue => (List<WarValue>)Ref!;
        public ClassData ClassValue => (ClassData)Ref!;

        /// <summary>
        /// Converts an F64 to int by truncating toward zero — matches the
        /// semantics of the old (int)double cast (D1). Used for array/string
        /// indices, repeat counts, etc. F64.FloorToInt floors toward -inf and
        /// would differ for negatives, so it must not be used in its place.
        /// </summary>
        public static int ToInt(F64 v) =>
            v.Raw >= 0 ? (int)(v.Raw >> 32) : -(int)((-v.Raw) >> 32);

        // ── Factory methods ──

        public static readonly WarValue Null = default;
        public static readonly WarValue True = new() { Tag = ValueTag.Logical, Numeric = F64.One };
        public static readonly WarValue False = new() { Tag = ValueTag.Logical, Numeric = F64.Zero };

        public static WarValue FromNumeric(F64 v) =>
            new() { Tag = ValueTag.Numeric, Numeric = v };

        // Convenience overloads so integer call-sites (counts, indices, enum
        // members, literal 0/1) stay clean WITHOUT re-admitting double/float.
        public static WarValue FromNumeric(int v) =>
            new() { Tag = ValueTag.Numeric, Numeric = F64.FromInt(v) };

        public static WarValue FromNumeric(long v) =>
            new() { Tag = ValueTag.Numeric, Numeric = F64.FromInt((int)v) };

        /// <summary>Build a numeric value straight from a fixed-point raw long
        /// (used by the bytecode deserializer).</summary>
        public static WarValue FromRawNumeric(long raw) =>
            new() { Tag = ValueTag.Numeric, Numeric = F64.FromRaw(raw) };

        public static WarValue FromLogical(bool v) =>
            new() { Tag = ValueTag.Logical, Numeric = v ? F64.One : F64.Zero };

        public static WarValue FromText(string v) =>
            new() { Tag = ValueTag.Text, Ref = v };

        public static WarValue FromArray(List<WarValue> v) =>
            new() { Tag = ValueTag.Array, Ref = v };

        public static WarValue FromClass(ClassData v) =>
            new() { Tag = ValueTag.Class, Ref = v };

        public static WarValue FromNativeObject(object v) =>
            new() { Tag = ValueTag.NativeObject, Ref = v };

        // ── Array helpers ──

        public WarValue GetArrayElement(int index)
        {
            var list = ArrayValue;
            return index >= 0 && index < list.Count ? list[index] : Null;
        }

        public void SetArrayElement(int index, WarValue value)
        {
            var list = ArrayValue;
            if (index >= 0 && index < list.Count)
                list[index] = value;
        }

        public void ArrayAppend(WarValue value) => ArrayValue.Add(value);

        // ── Text helpers ──

        public WarValue GetTextChar(int index)
        {
            var s = TextValue;
            return index >= 0 && index < s.Length
                ? FromText(s.Substring(index, 1))
                : Null;
        }

        public WarValue SetTextChar(int index, string replacement)
        {
            var s = TextValue;
            if (index >= 0 && index < s.Length)
                return FromText(s.Substring(0, index) + replacement + s.Substring(index + 1));
            return this;
        }

        // ── Equality ──

        /// <summary>
        /// How deep arrays and instances may nest inside one another for
        /// equality, hashing of state and formatting (plan PROP-5, T1.2).
        /// Equality beyond it raises LimitExceeded "Value nested too deeply";
        /// formatting prints <c>[...]</c>. Cyclic arrays reach it, so this is
        /// also how they are handled.
        /// </summary>
        public const int MaxValueDepth = 256;

        /// <summary>Message of the LimitExceeded error raised by equality beyond <see cref="MaxValueDepth"/>.</summary>
        internal const string ValueTooDeepMessage = "Value nested too deeply";

        // GetHashCode looks at most this many elements (or properties) of an
        // array (or instance), and this many levels deep. Equal values still
        // hash equal: Equals compares the counts and every element.
        private const int HashElements = 8;
        private const int HashDepth = 3;

        public bool Equals(WarValue other) => Equals(other, 0);

        /// <summary>
        /// Equality of a value nested <paramref name="depth"/> arrays or
        /// instances deep. Throws <see cref="WarScriptLimitException"/>, which
        /// the VM raises as a script error, when arrays or instances nest
        /// deeper than <see cref="MaxValueDepth"/> (CRASH-1).
        /// </summary>
        internal bool Equals(in WarValue other, int depth)
        {
            EqualityMemo? memo = null;
            return Equals(other, depth, ref memo, out _);
        }

        /// <summary>
        /// Equality with a memo of the array and instance pairs already found
        /// equal, so that arrays shared inside a value (a DAG such as
        /// <c>a = {a, a}</c> repeated) are compared once per pair instead of
        /// once per path, which is exponential (BUDGET-3). The memo is created
        /// on the first pair worth remembering, so flat values allocate
        /// nothing. <paramref name="height"/> is the number of array/instance
        /// levels the comparison walked, this value included (0 for a scalar):
        /// a memo hit raises the same depth error the walk would have.
        /// </summary>
        internal bool Equals(in WarValue other, int depth, ref EqualityMemo? memo, out int height)
        {
            height = 0;
            if (Tag != other.Tag) return false;
            switch (Tag)
            {
                case ValueTag.Null: return true;
                case ValueTag.Numeric: return Numeric == other.Numeric;
                case ValueTag.Logical: return Numeric == other.Numeric;
                case ValueTag.Text: return (string)Ref! == (string)other.Ref!;
                case ValueTag.Array:
                    if (depth >= MaxValueDepth)
                        throw new WarScriptLimitException(ValueTooDeepMessage);
                    var a = ArrayValue;
                    var b = other.ArrayValue;
                    if (a.Count != b.Count) return false;
                    if (memo != null && memo.TryGet(a, b, depth, out height)) return true;
                    var below = 0;
                    for (int i = 0; i < a.Count; i++)
                    {
                        if (!a[i].Equals(b[i], depth + 1, ref memo, out var h)) return false;
                        if (h > below) below = h;
                    }
                    height = below + 1;
                    if (below > 0) EqualityMemo.Add(ref memo, a, b, height);
                    return true;
                case ValueTag.Class:
                    if (depth >= MaxValueDepth)
                        throw new WarScriptLimitException(ValueTooDeepMessage);
                    return ClassValue.StructuralEquals(other.ClassValue, depth, ref memo, out height);
                case ValueTag.NativeObject:
                    return Ref != null && Ref.Equals(other.Ref);
                default: return false;
            }
        }

        /// <summary>
        /// Array and instance pairs one equality comparison has already found
        /// equal, with the height of each pair's comparison (see
        /// <see cref="Equals(in WarValue, int, ref EqualityMemo?, out int)"/>).
        /// Only pairs with nested arrays or instances are recorded. Lives for
        /// one comparison; no static state (R1).
        /// </summary>
        internal sealed class EqualityMemo
        {
            private readonly Dictionary<(object, object), int> _equal =
                new Dictionary<(object, object), int>(PairComparer.Instance);

            internal static void Add(ref EqualityMemo? memo, object a, object b, int height)
            {
                memo ??= new EqualityMemo();
                memo._equal[(a, b)] = height;
            }

            /// <summary>
            /// True when (a, b) is known equal; throws the depth error the
            /// comparison would have raised at <paramref name="depth"/>.
            /// </summary>
            internal bool TryGet(object a, object b, int depth, out int height)
            {
                if (!_equal.TryGetValue((a, b), out height)) return false;
                if (depth + height > MaxValueDepth)
                    throw new WarScriptLimitException(ValueTooDeepMessage);
                return true;
            }

            private sealed class PairComparer : IEqualityComparer<(object, object)>
            {
                public static readonly PairComparer Instance = new PairComparer();

                public bool Equals((object, object) x, (object, object) y) =>
                    ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

                public int GetHashCode((object, object) p) =>
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.Item1) * 31
                    + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.Item2);
            }
        }

        public override bool Equals(object? obj) =>
            obj is WarValue other && Equals(other);

        public override int GetHashCode() => GetHashCode(0);

        /// <summary>
        /// Hash of a value nested <paramref name="depth"/> arrays or
        /// instances deep: at most the first 8 elements of an array, and
        /// nothing below 3 levels, so cyclic and deeply nested values hash in
        /// bounded time.
        /// </summary>
        internal int GetHashCode(int depth)
        {
            switch (Tag)
            {
                case ValueTag.Null: return 0;
                case ValueTag.Numeric: return Numeric.GetHashCode();
                case ValueTag.Logical: return LogicalValue.GetHashCode();
                case ValueTag.Text: return TextValue.GetHashCode();
                case ValueTag.Array:
                    var list = ArrayValue;
                    var hash = 17 * 31 + list.Count;
                    if (depth >= HashDepth) return hash;
                    var n = Math.Min(list.Count, HashElements);
                    for (int i = 0; i < n; i++) hash = hash * 31 + list[i].GetHashCode(depth + 1);
                    return hash;
                case ValueTag.Class:
                    return depth >= HashDepth ? 17 : ClassValue.StructuralHashCode(depth);
                case ValueTag.NativeObject: return Ref?.GetHashCode() ?? 0;
                default: return 0;
            }
        }

        // ── ToString ──

        public override string ToString() => Format(null);

        /// <summary>
        /// Formats the value by the canonical rules (v2 plan 5.10, T1.6): the
        /// same text in every culture, numbers through integer arithmetic only.
        /// A native object is formatted by <paramref name="formatters"/> when
        /// one is registered for its type (the instance's, for <c>print</c> and
        /// text operators), otherwise as <c>&lt;TypeName&gt;</c>.
        /// </summary>
        internal string Format(NativeFormatters? formatters)
        {
            switch (Tag)
            {
                case ValueTag.Null: return "null";
                case ValueTag.Numeric: return FormatNumber(Numeric);
                case ValueTag.Logical: return LogicalValue ? "True" : "False";
                case ValueTag.Text: return TextValue;
                case ValueTag.Array:
                    var sb = new StringBuilder();
                    AppendArray(sb, ArrayValue, 0, new List<List<WarValue>>(), formatters);
                    return sb.ToString();
                case ValueTag.Class:
                    return ClassValue.Definition.ClassDetails.Name;
                case ValueTag.NativeObject:
                    return FormatNative(Ref, formatters);
                default: return "";
            }
        }

        /// <summary>
        /// A native object (PROP-14): never the host object's own
        /// <c>ToString()</c>, which may go through <c>double</c> or the
        /// current culture (DET-3). <c>F64Vec3</c> prints as <c>(x, y, z)</c>
        /// with the number rules, a function value as <c>&lt;fun name&gt;</c>
        /// (a lambda as <c>&lt;fun lambda&gt;</c>), anything else as the
        /// registered formatter's text or <c>&lt;TypeName&gt;</c>.
        /// </summary>
        private static string FormatNative(object? obj, NativeFormatters? formatters)
        {
            switch (obj)
            {
                case null:
                    return "null";
                case F64Vec3 v:
                    return "(" + FormatNumber(v.X) + ", " + FormatNumber(v.Y) + ", " + FormatNumber(v.Z) + ")";
                case CompiledFunction f:
                    return "<fun " + (f.Name == "<lambda>" ? "lambda" : f.Name) + ">";
            }
            if (formatters != null && !formatters.IsEmpty && formatters.TryFormat(obj, out var text))
                return text;
            return "<" + obj.GetType().Name + ">";
        }

        /// <summary>
        /// Formats a number (5.10): an integer as invariant digits with an
        /// ASCII '-', a fraction as the shortest decimal of up to 9 fractional
        /// digits that the literal parser reads back to the same raw value,
        /// or else its exact decimal expansion. No <c>double</c>, no culture.
        /// </summary>
        public static string FormatNumber(F64 v)
        {
            var interned = StringInterner.TryGetIntegerString(v);
            if (interned != null) return interned;
            long raw = v.Raw;
            if ((raw & 0xFFFFFFFFL) == 0)
                return (raw >> 32).ToString(CultureInfo.InvariantCulture); // exact for negatives too
            return FormatFractional(raw);
        }

        /// <summary>
        /// Formatting an array stops adding elements once the text reaches
        /// this many characters and writes <c>...</c> in place of the rest
        /// (BUDGET-3): arrays shared inside an array format once per path, so
        /// 40 levels of <c>a = {a, a}</c> would otherwise format 2^40
        /// elements. The same value as the default
        /// <see cref="WarScriptLanguage.MaxTextLength"/>; a constant because
        /// formatting has no instance to read a setting from.
        /// </summary>
        public const int MaxFormattedLength = 1_048_576;

        /// <summary>
        /// Formats an array nested <paramref name="depth"/> arrays deep.
        /// <paramref name="inProgress"/> holds the arrays being formatted
        /// around it; an array already among them, or one nested deeper than
        /// <see cref="MaxValueDepth"/>, prints as <c>[...]</c> (CRASH-1). Past
        /// <see cref="MaxFormattedLength"/> characters the remaining elements
        /// of every open array print as <c>...</c>.
        /// </summary>
        private static void AppendArray(StringBuilder sb, List<WarValue> vals, int depth,
            List<List<WarValue>> inProgress, NativeFormatters? formatters)
        {
            if (depth >= MaxValueDepth || ContainsReference(inProgress, vals))
            {
                sb.Append("[...]");
                return;
            }
            inProgress.Add(vals);
            sb.Append('[');
            for (int i = 0; i < vals.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                if (sb.Length >= MaxFormattedLength)
                {
                    sb.Append("...");
                    break;
                }
                var v = vals[i];
                if (v.Tag == ValueTag.Array)
                    AppendArray(sb, v.ArrayValue, depth + 1, inProgress, formatters);
                else
                    sb.Append(v.Format(formatters));
            }
            sb.Append(']');
            inProgress.RemoveAt(inProgress.Count - 1);
        }

        private static bool ContainsReference(List<List<WarValue>> lists, List<WarValue> list)
        {
            for (int i = 0; i < lists.Count; i++)
                if (ReferenceEquals(lists[i], list)) return true;
            return false;
        }

        private static readonly ulong[] Pow10 =
            { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };

        /// <summary>
        /// A number with a fractional part (5.10, T1.6). For p = 1..9 the
        /// p-digit decimal rounded to nearest is a candidate; the first one
        /// <see cref="NumericLiteral.TryParseRaw"/> reads back to
        /// <paramref name="raw"/> is the result, so the literal <c>3.14</c>
        /// prints as <c>3.14</c> rather than its binary expansion. When none
        /// does, the exact expansion of the 32-bit fraction is written
        /// (multiply by 10, emit the integer digit, keep the remainder), which
        /// ends within 32 digits: 1/3 prints as
        /// <c>0.33333333325572311878204345703125</c>. Integer arithmetic only,
        /// on the magnitude as an unsigned value, so <c>F64.MinValue</c> does
        /// not overflow.
        /// </summary>
        private static string FormatFractional(long raw)
        {
            bool neg = raw < 0;
            ulong mag = neg ? unchecked(0UL - (ulong)raw) : (ulong)raw;
            ulong intPart = mag >> 32;
            ulong frac = mag & 0xFFFFFFFFUL;
            string sign = neg ? "-" : "";
            for (int p = 1; p <= 9; p++)
            {
                ulong pow = Pow10[p];
                ulong scaled = (frac * pow + (1UL << 31)) >> 32; // round to nearest
                ulong ip = intPart;
                if (scaled >= pow) { ip++; scaled -= pow; }      // rounding carry
                string candidate = sign + ip.ToString(CultureInfo.InvariantCulture) + "."
                                   + scaled.ToString(CultureInfo.InvariantCulture).PadLeft(p, '0');
                // An out-of-range candidate (a carry past 2147483647) is rejected, not thrown.
                if (WarScript.Parser.NumericLiteral.TryParseRaw(candidate, out long parsed) && parsed == raw)
                    return candidate;
            }

            var sb = new StringBuilder(sign.Length + 11 + 1 + 32);
            sb.Append(sign).Append(intPart.ToString(CultureInfo.InvariantCulture)).Append('.');
            while (frac != 0)
            {
                frac *= 10;                          // < 2^36: no overflow
                sb.Append((char)('0' + (int)(frac >> 32)));
                frac &= 0xFFFFFFFFUL;
            }
            return sb.ToString();
        }

        // ── Comparison helpers (used by comparison operators) ──

        public int CompareTo(WarValue other)
        {
            if (Tag == other.Tag)
            {
                switch (Tag)
                {
                    case ValueTag.Numeric: return Numeric.CompareTo(other.Numeric);
                    case ValueTag.Logical: return Numeric.CompareTo(other.Numeric);
                    case ValueTag.Text: return string.Compare(TextValue, other.TextValue, StringComparison.Ordinal);
                }
            }
            return string.Compare(ToString(), other.ToString(), StringComparison.Ordinal);
        }

        // ── String repetition helper ──

        public static string RepeatString(string s, int count)
        {
            if (count <= 0 || s.Length == 0) return string.Empty;
            if (count == 1) return s;
            return string.Create(s.Length * count, s, (span, src) =>
            {
                for (var i = 0; i < span.Length; i += src.Length)
                    src.AsSpan().CopyTo(span.Slice(i));
            });
        }
    }
}
