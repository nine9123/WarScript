#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WarScript.Context;
using WarScript.Expression.Value;

namespace WarScript
{
    /// <summary>
    /// The canonical state hash of an instance (v2 plan, section 9.7): a 64-bit
    /// FNV-1a hash over the printed lines, then the byte 0xFF, then every user
    /// global sorted by name (ordinal), each written as its name's UTF-16
    /// code units, 0x00 0x00, and the value's encoding. All multi-byte
    /// quantities are little-endian, independent of the platform.
    ///
    /// A value is its <see cref="ValueTag"/> byte followed by:
    /// null — nothing; number — the 8-byte raw; logical — 1 byte; text — a
    /// 4-byte length and the UTF-16 code units; array — a 4-byte count and the
    /// elements; instance — the class name encoded as a text value, then its
    /// fields; native object — its formatting encoded as a text value. An array
    /// or instance already being encoded is written as <see cref="CycleMarker"/>,
    /// and one nested deeper than <see cref="MaxValueDepth"/> as
    /// <see cref="DepthMarker"/>.
    ///
    /// v1 instances have no declared field list yet (T3.4 adds one), so an
    /// instance's fields are written as: a 4-byte count of its fields, the
    /// constructor parameters' values in declaration order, then every other
    /// field of the instance sorted by name (ordinal), each as name, 0x00 0x00,
    /// value.
    /// </summary>
    internal static class StateHash
    {
        public const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public const byte GlobalsSeparator = 0xFF;
        public const byte CycleMarker = 0xFE;
        public const byte DepthMarker = 0xFD;

        /// <summary>Plan PROP-5. Becomes the instance-wide limit in T1.2.</summary>
        public const int MaxValueDepth = 256;

        /// <summary>Continues <paramref name="hash"/> with one printed line.</summary>
        public static ulong AppendLine(ulong hash, string line)
        {
            var h = new Hasher(hash);
            h.Chars(line);
            h.Byte(0x0A);
            h.Byte(0x00);
            return h.Hash;
        }

        /// <summary>
        /// Finishes the hash: <paramref name="printedHash"/> already covers the
        /// printed lines; the separator and the globals of
        /// <paramref name="globals"/> are added to it.
        /// </summary>
        public static ulong Compute(ulong printedHash, MemoryScope globals)
        {
            var h = new Hasher(printedHash);
            h.Byte(GlobalsSeparator);

            var variables = globals.GetAllLocals();
            var names = new List<string>(variables.Keys);
            names.Sort(System.StringComparer.Ordinal);

            var inProgress = new HashSet<object>(ReferenceComparer.Instance);
            foreach (var name in names)
            {
                h.Chars(name);
                h.Byte(0x00);
                h.Byte(0x00);
                h.Value(variables[name], 0, inProgress);
            }
            return h.Hash;
        }

        private struct Hasher
        {
            public ulong Hash;

            public Hasher(ulong start) { Hash = start; }

            public void Byte(byte b)
            {
                Hash ^= b;
                Hash *= Prime;
            }

            public void Int32(int v)
            {
                for (var i = 0; i < 4; i++)
                    Byte((byte)(v >> (8 * i)));
            }

            public void Int64(long v)
            {
                for (var i = 0; i < 8; i++)
                    Byte((byte)(v >> (8 * i)));
            }

            public void Chars(string s)
            {
                foreach (var c in s)
                {
                    Byte((byte)c);
                    Byte((byte)(c >> 8));
                }
            }

            public void Text(string s)
            {
                Byte((byte)ValueTag.Text);
                Int32(s.Length);
                Chars(s);
            }

            public void Value(in WarValue value, int depth, HashSet<object> inProgress)
            {
                switch (value.Tag)
                {
                    case ValueTag.Null:
                        Byte((byte)ValueTag.Null);
                        return;
                    case ValueTag.Numeric:
                        Byte((byte)ValueTag.Numeric);
                        Int64(value.Numeric.Raw);
                        return;
                    case ValueTag.Logical:
                        Byte((byte)ValueTag.Logical);
                        Byte(value.LogicalValue ? (byte)1 : (byte)0);
                        return;
                    case ValueTag.Text:
                        Text(value.TextValue);
                        return;
                    case ValueTag.NativeObject:
                        Text(value.ToString());
                        return;
                }

                // Array or instance: guard against cycles and runaway depth.
                if (depth >= MaxValueDepth)
                {
                    Byte(DepthMarker);
                    return;
                }
                if (!inProgress.Add(value.Ref!))
                {
                    Byte(CycleMarker);
                    return;
                }

                if (value.Tag == ValueTag.Array)
                {
                    var elements = value.ArrayValue;
                    Byte((byte)ValueTag.Array);
                    Int32(elements.Count);
                    foreach (var element in elements)
                        Value(element, depth + 1, inProgress);
                }
                else
                {
                    var instance = value.ClassValue;
                    Byte((byte)ValueTag.Class);
                    Text(instance.Definition.ClassDetails.Name);

                    var fields = instance.MemoryScope.GetAllLocals();
                    Int32(fields.Count);

                    var declared = instance.Definition.ClassDetails.Properties;
                    foreach (var name in declared)
                        Value(fields.TryGetValue(name, out var v) ? v : WarValue.Null, depth + 1, inProgress);

                    var others = new List<string>();
                    foreach (var name in fields.Keys)
                        if (!declared.Contains(name))
                            others.Add(name);
                    others.Sort(System.StringComparer.Ordinal);
                    foreach (var name in others)
                    {
                        Chars(name);
                        Byte(0x00);
                        Byte(0x00);
                        Value(fields[name], depth + 1, inProgress);
                    }
                }

                inProgress.Remove(value.Ref!);
            }
        }

        /// <summary>Reference identity (ReferenceEqualityComparer is not in netstandard2.1).</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
