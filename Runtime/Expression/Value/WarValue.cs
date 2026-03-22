#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

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
        /// Holds numeric value (Tag.Numeric) or encoded boolean (Tag.Logical: 1.0=true, 0.0=false).
        /// </summary>
        public double Numeric;

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

        public double NumericValue => Numeric;
        public bool LogicalValue => Numeric != 0;
        public string TextValue => (string)Ref!;
        public List<WarValue> ArrayValue => (List<WarValue>)Ref!;
        public ClassData ClassValue => (ClassData)Ref!;

        // ── Factory methods ──

        public static readonly WarValue Null = default;
        public static readonly WarValue True = new() { Tag = ValueTag.Logical, Numeric = 1.0 };
        public static readonly WarValue False = new() { Tag = ValueTag.Logical, Numeric = 0.0 };

        public static WarValue FromNumeric(double v) =>
            new() { Tag = ValueTag.Numeric, Numeric = v };

        public static WarValue FromLogical(bool v) =>
            new() { Tag = ValueTag.Logical, Numeric = v ? 1.0 : 0.0 };

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

        public bool Equals(WarValue other)
        {
            if (Tag != other.Tag) return false;
            switch (Tag)
            {
                case ValueTag.Null: return true;
                case ValueTag.Numeric: return Numeric == other.Numeric;
                case ValueTag.Logical: return Numeric == other.Numeric;
                case ValueTag.Text: return (string)Ref! == (string)other.Ref!;
                case ValueTag.Array:
                    var a = ArrayValue;
                    var b = other.ArrayValue;
                    if (a.Count != b.Count) return false;
                    for (int i = 0; i < a.Count; i++)
                        if (!a[i].Equals(b[i])) return false;
                    return true;
                case ValueTag.Class:
                    return ClassValue.StructuralEquals(other.ClassValue);
                case ValueTag.NativeObject:
                    return Ref != null && Ref.Equals(other.Ref);
                default: return false;
            }
        }

        public override bool Equals(object? obj) =>
            obj is WarValue other && Equals(other);

        public override int GetHashCode()
        {
            switch (Tag)
            {
                case ValueTag.Null: return 0;
                case ValueTag.Numeric: return Numeric.GetHashCode();
                case ValueTag.Logical: return LogicalValue.GetHashCode();
                case ValueTag.Text: return TextValue.GetHashCode();
                case ValueTag.Array:
                    var hash = 17;
                    foreach (var v in ArrayValue) hash = hash * 31 + v.GetHashCode();
                    return hash;
                case ValueTag.Class: return ClassValue.StructuralHashCode();
                case ValueTag.NativeObject: return Ref?.GetHashCode() ?? 0;
                default: return 0;
            }
        }

        // ── ToString ──

        public override string ToString()
        {
            switch (Tag)
            {
                case ValueTag.Null: return "null";
                case ValueTag.Numeric:
                    return Numeric % 1 == 0 ? ((int)Numeric).ToString() : Numeric.ToString();
                case ValueTag.Logical: return LogicalValue ? "True" : "False";
                case ValueTag.Text: return TextValue;
                case ValueTag.Array:
                    var sb = new StringBuilder("[");
                    var vals = ArrayValue;
                    for (int i = 0; i < vals.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(vals[i].ToString());
                    }
                    sb.Append(']');
                    return sb.ToString();
                case ValueTag.Class:
                    return ClassValue.Definition.ClassDetails.Name;
                case ValueTag.NativeObject:
                    return Ref?.ToString() ?? "null";
                default: return "";
            }
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
