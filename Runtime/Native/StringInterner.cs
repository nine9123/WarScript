#nullable enable

using System.Collections.Generic;

namespace WarScript
{
    /// <summary>
    /// Two-tier string interner for reducing GC pressure in the VM.
    ///
    /// Tier 1: Static table of small integer strings ("0" through "999").
    ///         These appear constantly in game scripts (scores, hp, indices).
    ///         Zero-cost lookup — just an array index.
    ///
    /// Tier 2: Bounded runtime cache for short dynamically-created strings
    ///         (≤ MaxInternLength chars). Uses a dictionary for O(1) lookup.
    ///         When the cache exceeds MaxCacheSize, it's cleared — this is
    ///         a performance cache, not a correctness requirement.
    ///
    /// Strings longer than MaxInternLength are returned as-is (no interning).
    /// The interner never modifies strings — it only deduplicates references.
    /// </summary>
    public class StringInterner
    {
        /// <summary>Maximum string length to consider for interning.</summary>
        public const int MaxInternLength = 64;

        /// <summary>Maximum entries in the runtime cache before eviction.</summary>
        public const int MaxCacheSize = 4096;

        // ── Tier 1: static integer strings ──
        private const int IntegerRangeMax = 1000;
        private static readonly string[] IntegerStrings;

        static StringInterner()
        {
            IntegerStrings = new string[IntegerRangeMax];
            for (int i = 0; i < IntegerRangeMax; i++)
                IntegerStrings[i] = i.ToString();
        }

        // ── Tier 2: runtime cache ──
        private readonly Dictionary<string, string> _cache = new(MaxCacheSize);

        /// <summary>
        /// Intern a string. Returns the canonical reference for this string value.
        /// For strings that match a small integer (0-999), returns the static instance.
        /// For short strings, returns a cached reference.
        /// For long strings, returns the input unchanged.
        /// </summary>
        public string Intern(string s)
        {
            if (s.Length > MaxInternLength)
                return s;

            // Check tier-2 cache first (covers all short strings including integers)
            if (_cache.TryGetValue(s, out var cached))
                return cached;

            // Evict if full
            if (_cache.Count >= MaxCacheSize)
                _cache.Clear();

            _cache[s] = s;
            return s;
        }

        /// <summary>
        /// Fast path for numeric ToString: if the number is a small non-negative
        /// integer, return the pre-allocated string directly without hashing.
        /// </summary>
        public static string? TryGetIntegerString(double value)
        {
            if (value >= 0 && value < IntegerRangeMax && value % 1 == 0)
                return IntegerStrings[(int)value];
            return null;
        }
    }
}
