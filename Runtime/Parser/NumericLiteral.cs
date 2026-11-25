#nullable enable

using FixMath;
using WarScript.Exception;

namespace WarScript.Parser
{
    /// <summary>
    /// Parses WarScript numeric literals into deterministic 32.32 fixed-point
    /// <see cref="F64"/> values using <b>integer arithmetic only</b> — no float
    /// or double is touched at any point, so the produced raw value is bit-identical
    /// on every platform/architecture (x86-64, ARM; Windows, macOS, Linux/SteamDeck).
    ///
    /// Grammar (underscores are already stripped by the lexer):
    ///   literal := '-'? digit+ ('.' digit+)?
    ///
    /// Malformed input throws <see cref="SyntaxException"/> (determinism guarantee D2a):
    /// the parser never silently coerces or guesses.
    ///
    /// Fractional precision beyond 9 digits is deterministically truncated
    /// (9 digits is the most that fits the intermediate <c>fracNum &lt;&lt; 32</c>
    /// inside a signed 64-bit value without overflow).
    /// </summary>
    public static class NumericLiteral
    {
        private const int Shift = 32;                 // Fixed64 fractional bits
        private const long MaxIntegerPart = 2147483647L; // 2^31 - 1; larger overflows raw on <<32

        public static F64 Parse(string s)
        {
            var error = TryParseCore(s, out long raw, out char bad);
            switch (error)
            {
                case Error.None:
                    return F64.FromRaw(raw);
                case Error.Empty:
                    throw new SyntaxException("Empty numeric literal");
                case Error.InvalidCharacter:
                    throw new SyntaxException($"Invalid character '{bad}' in numeric literal '{s}'");
                case Error.OutOfRange:
                    throw new SyntaxException($"Numeric literal '{s}' is out of the representable F64 range");
                default:
                    throw new SyntaxException($"Malformed numeric literal '{s}'");
            }
        }

        /// <summary>
        /// Parses <paramref name="s"/> exactly as <see cref="Parse"/> does, but
        /// returns false instead of throwing when it is not a valid literal
        /// (v2 plan T1.6: the number formatter checks its candidates with it).
        /// </summary>
        public static bool TryParseRaw(string s, out long raw) =>
            TryParseCore(s, out raw, out _) == Error.None;

        private enum Error { None, Empty, InvalidCharacter, OutOfRange, Malformed }

        private static Error TryParseCore(string s, out long raw, out char bad)
        {
            raw = 0;
            bad = '\0';
            if (string.IsNullOrEmpty(s))
                return Error.Empty;

            int i = 0;
            bool negative = false;
            if (s[0] == '-') { negative = true; i = 1; }
            else if (s[0] == '+') { i = 1; } // lexer doesn't emit '+', tolerated defensively

            // ── Integer part ──
            long intPart = 0;
            int intDigits = 0;
            while (i < s.Length && s[i] != '.')
            {
                char c = s[i];
                if (c < '0' || c > '9') { bad = c; return Error.InvalidCharacter; }
                intPart = intPart * 10 + (c - '0');
                if (intPart > MaxIntegerPart)
                    return Error.OutOfRange;
                intDigits++;
                i++;
            }

            // ── Fractional part ──
            long fracRaw = 0;
            int fracDigits = 0;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                long fracNum = 0;   // accumulated fractional numerator
                long fracDen = 1;   // 10^fracDigits
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c < '0' || c > '9') { bad = c; return Error.InvalidCharacter; }
                    if (fracDigits < 9)
                    {
                        fracNum = fracNum * 10 + (c - '0');
                        fracDen *= 10;
                        fracDigits++;
                    }
                    // digits past the 9th are deterministically truncated
                    i++;
                }
                if (fracDigits > 0)
                    fracRaw = (fracNum << Shift) / fracDen; // exact integer division
            }

            if (intDigits == 0 && fracDigits == 0)
                return Error.Malformed;

            raw = (intPart << Shift) + fracRaw;
            if (negative) raw = -raw;
            return Error.None;
        }
    }
}
