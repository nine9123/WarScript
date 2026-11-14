using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WarScript.Fuzz
{
    /// <summary>
    /// SplitMix64. The fuzzer's only randomness: a case is fully determined by
    /// its seed and the corpus, on every runtime (System.Random's sequence is
    /// not part of its contract).
    /// </summary>
    internal sealed class Prng
    {
        private ulong _state;

        public Prng(ulong seed) { _state = seed; }

        public ulong NextULong()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>0 &lt;= result &lt; <paramref name="max"/> (max &gt; 0).</summary>
        public int Next(int max) => (int)(NextULong() % (ulong)max);

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

        /// <summary>The seed of case <paramref name="index"/> of a run started with <paramref name="master"/>.</summary>
        public static ulong CaseSeed(ulong master, long index) =>
            new Prng(master ^ ((ulong)index * 0xD1B54A32D192ED03UL)).NextULong();
    }

    /// <summary>
    /// The bytecode mutations of plan 9.6 (T1.7): one case in eight truncates
    /// the file at a random length, the others set 1 to 8 random bytes after
    /// the header to random values. The 9-byte header (magic, version, format
    /// hash) is kept, or nearly every case would stop at the header check.
    /// </summary>
    internal static class BytecodeMutator
    {
        private const int HeaderLength = 9;

        public static byte[] Mutate(byte[] original, Prng rng, List<string> log)
        {
            if (original.Length <= HeaderLength || rng.Next(8) == 0)
            {
                var length = rng.Next(original.Length);
                log.Add($"truncate to {length} of {original.Length} bytes");
                var cut = new byte[length];
                Array.Copy(original, cut, length);
                return cut;
            }
            var bytes = (byte[])original.Clone();
            for (var k = 1 + rng.Next(8); k > 0; k--)
            {
                var at = HeaderLength + rng.Next(bytes.Length - HeaderLength);
                var value = (byte)rng.Next(256);
                log.Add($"byte {at}: 0x{bytes[at]:X2} -> 0x{value:X2}");
                bytes[at] = value;
            }
            return bytes;
        }
    }

    internal enum Kind { Space, Newline, Comment, Text, Number, Identifier, Operator }

    internal readonly struct SurfaceToken
    {
        public readonly Kind Kind;
        public readonly string Value;

        public SurfaceToken(Kind kind, string value) { Kind = kind; Value = value; }

        public bool Significant => Kind != Kind.Space && Kind != Kind.Newline && Kind != Kind.Comment;
        public override string ToString() => Value;
    }

    /// <summary>
    /// The source mutations of plan 9.6. They work on a surface tokenization
    /// of their own rather than on <c>LexicalParser</c>'s tokens: the fuzzer
    /// must not lex in its own process (a lexer crash would take the fuzzer
    /// down instead of being reported), and the lexer's tokens cannot be
    /// turned back into source (escapes and interpolation are folded at lex
    /// time). The tokenizer only has to be good enough for useful mutations;
    /// whatever it gets wrong just yields more syntax errors.
    /// </summary>
    internal static class Mutator
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "elif", "else", "end", "print", "class", "fun", "return", "loop", "in", "by",
            "break", "next", "assert", "raise", "begin", "rescue", "ensure", "import", "yield",
            "const", "enum", "new", "and", "or", "as", "is", "true", "false", "null", "this"
        };

        private static readonly string[] Openers = { "if", "loop", "fun", "class", "enum", "begin" };
        private static readonly string[] Middles = { "elif", "else", "rescue", "ensure" };

        private static readonly string[] Boundaries =
        {
            "0", "-1", "2147483647", "-2147483648", "0.000000001", "\"\"",
            "\"" + new string('x', 10_000) + "\"", "{}"
        };

        private static readonly string[] MultiCharOperators =
            { "::", "<<", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", ".." };

        /// <summary>
        /// Applies 1 to 3 mutations to <paramref name="source"/>; each applied
        /// mutation is described in <paramref name="log"/>. Comments are dropped
        /// and '#' replaced, so the runner never reads a directive from a case
        /// except the header the fuzzer writes itself.
        /// </summary>
        public static string Mutate(string source, IReadOnlyList<string> corpus, Prng rng, List<string> log)
        {
            var text = Render(Tokenize(source).Where(t => t.Kind != Kind.Comment));
            var count = 1 + rng.Next(3);
            for (var i = 0; i < count; i++)
            {
                string description = null;
                // A mutation that finds nothing to work on returns null; try another.
                for (var attempt = 0; attempt < 8 && description == null; attempt++)
                {
                    var result = Apply(rng.Next(8), text, corpus, rng, out description);
                    if (description != null) text = result;
                }
                if (description != null) log.Add(description);
            }
            return text.Replace('#', '_');
        }

        private static string Apply(int which, string text, IReadOnlyList<string> corpus, Prng rng, out string description)
        {
            switch (which)
            {
                case 0: return DeleteToken(text, rng, out description);
                case 1: return DuplicateToken(text, rng, out description);
                case 2: return SwapTokens(text, rng, out description);
                case 3: return ReplaceIdentifier(text, rng, out description);
                case 4: return ReplaceLiteral(text, rng, out description);
                case 5: return InsertSelfAppend(text, rng, out description);
                case 6: return SpliceStatement(text, corpus, rng, out description);
                default: return Wrap(text, rng, out description);
            }
        }

        // ── Token mutations ─────────────────────────────────────────────

        private static string DeleteToken(string text, Prng rng, out string description)
        {
            var tokens = Tokenize(text);
            var at = PickIndex(tokens, t => t.Significant, rng);
            description = at < 0 ? null : $"delete token '{Short(tokens[at].Value)}'";
            if (at < 0) return text;
            tokens.RemoveAt(at);
            return Render(tokens);
        }

        private static string DuplicateToken(string text, Prng rng, out string description)
        {
            var tokens = Tokenize(text);
            var at = PickIndex(tokens, t => t.Significant, rng);
            description = at < 0 ? null : $"duplicate token '{Short(tokens[at].Value)}'";
            if (at < 0) return text;
            tokens.Insert(at + 1, tokens[at]);
            tokens.Insert(at + 1, new SurfaceToken(Kind.Space, " "));
            return Render(tokens);
        }

        private static string SwapTokens(string text, Prng rng, out string description)
        {
            var tokens = Tokenize(text);
            var a = PickIndex(tokens, t => t.Significant, rng);
            var b = PickIndex(tokens, t => t.Significant, rng);
            description = a < 0 || a == b ? null : $"swap tokens '{Short(tokens[a].Value)}' and '{Short(tokens[b].Value)}'";
            if (description == null) return text;
            var t0 = tokens[a];
            tokens[a] = tokens[b];
            tokens[b] = t0;
            return Render(tokens);
        }

        private static string ReplaceIdentifier(string text, Prng rng, out string description)
        {
            var tokens = Tokenize(text);
            var names = Names(tokens);
            var at = PickIndex(tokens, IsName, rng);
            description = null;
            if (at < 0 || names.Count < 2) return text;
            var name = rng.Pick(names);
            if (name == tokens[at].Value) return text;
            description = $"replace identifier '{tokens[at].Value}' with '{name}'";
            tokens[at] = new SurfaceToken(Kind.Identifier, name);
            return Render(tokens);
        }

        private static string ReplaceLiteral(string text, Prng rng, out string description)
        {
            var tokens = Tokenize(text);
            var at = PickIndex(tokens, t => t.Kind == Kind.Number || t.Kind == Kind.Text, rng);
            description = null;
            if (at < 0) return text;
            var value = rng.Pick(Boundaries);
            description = $"replace literal '{Short(tokens[at].Value)}' with '{Short(value)}'";
            tokens[at] = new SurfaceToken(Kind.Text, value);
            return Render(tokens);
        }

        // ── Statement mutations ─────────────────────────────────────────

        private static string InsertSelfAppend(string text, Prng rng, out string description)
        {
            var names = Names(Tokenize(text));
            description = null;
            if (names.Count == 0) return text;
            var name = rng.Pick(names);
            var lines = Lines(text);
            var at = rng.Next(lines.Count + 1);
            description = $"insert '{name} << {name}' before line {at + 1}";
            lines.Insert(at, $"{name} << {name}");
            return string.Join("\n", lines);
        }

        private static string SpliceStatement(string text, IReadOnlyList<string> corpus, Prng rng, out string description)
        {
            description = null;
            if (corpus.Count == 0) return text;
            var donor = Lines(Render(Tokenize(rng.Pick(corpus)).Where(t => t.Kind != Kind.Comment)))
                .Where(l => l.Trim().Length > 0 && Depth(l).delta == 0 && Depth(l).min >= 0).ToList();
            if (donor.Count == 0) return text;
            var statement = rng.Pick(donor).Trim();
            var lines = Lines(text);
            var at = rng.Next(lines.Count + 1);
            description = $"splice '{Short(statement)}' before line {at + 1}";
            lines.Insert(at, statement);
            return string.Join("\n", lines);
        }

        private static string Wrap(string text, Prng rng, out string description)
        {
            description = null;
            var lines = Lines(text);
            if (lines.Count == 0) return text;

            // A range of whole statements: its block keywords balance and it
            // never closes a block it did not open.
            var start = rng.Next(lines.Count);
            var ends = new List<int>();
            var depth = 0;
            for (var i = start; i < lines.Count; i++)
            {
                var (delta, min) = Depth(lines[i]);
                if (depth + min < 0) break;
                depth += delta;
                if (depth == 0 && lines[i].Trim().Length > 0) ends.Add(i);
            }
            if (ends.Count == 0) return text;
            var end = rng.Pick(ends);
            var body = lines.GetRange(start, end - start + 1);

            List<string> wrapped;
            switch (rng.Next(3))
            {
                case 0:
                    description = $"wrap lines {start + 1}-{end + 1} in begin/rescue";
                    wrapped = new List<string> { "begin" };
                    wrapped.AddRange(body);
                    wrapped.Add("rescue fz_err");
                    wrapped.Add("end");
                    break;
                case 1:
                    description = $"wrap lines {start + 1}-{end + 1} in a 0..1000 loop";
                    wrapped = new List<string> { "loop fz_i in 0..1000" };
                    wrapped.AddRange(body);
                    wrapped.Add("end");
                    break;
                default:
                    var calls = rng.Pick(new[] { 2, 50, 1000 });
                    description = $"wrap lines {start + 1}-{end + 1} in a function that calls itself {calls} deep";
                    wrapped = new List<string> { "fun fz_rec [fz_d]", "if fz_d > 0", "fz_rec [fz_d - 1]", "end" };
                    wrapped.AddRange(body);
                    wrapped.Add("end");
                    wrapped.Add($"fz_rec [{calls}]");
                    break;
            }
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, wrapped);
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The net change in block depth over a line, and the lowest depth
        /// reached within it (relative to its start). elif/else/rescue/ensure
        /// close a block and open the next one.
        /// </summary>
        private static (int delta, int min) Depth(string line)
        {
            int depth = 0, min = 0;
            foreach (var t in Tokenize(line))
            {
                if (t.Kind != Kind.Identifier) continue;
                if (Array.IndexOf(Openers, t.Value) >= 0) depth++;
                else if (t.Value == "end") { depth--; min = Math.Min(min, depth); }
                else if (Array.IndexOf(Middles, t.Value) >= 0) { min = Math.Min(min, depth - 1); }
            }
            return (depth, min);
        }

        // ── Surface tokenizer ───────────────────────────────────────────

        public static List<SurfaceToken> Tokenize(string s)
        {
            var tokens = new List<SurfaceToken>();
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                var start = i;
                Kind kind;
                if (c == '\n') { i++; kind = Kind.Newline; }
                else if (char.IsWhiteSpace(c)) { while (i < s.Length && s[i] != '\n' && char.IsWhiteSpace(s[i])) i++; kind = Kind.Space; }
                else if (c == '#') { while (i < s.Length && s[i] != '\n') i++; kind = Kind.Comment; }
                else if (c == '"' || (c == '$' && i + 1 < s.Length && s[i + 1] == '"')) { i = SkipText(s, i); kind = Kind.Text; }
                else if (char.IsDigit(c))
                {
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                    if (i + 1 < s.Length && s[i] == '.' && char.IsDigit(s[i + 1]))
                    {
                        i++;
                        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                    }
                    kind = Kind.Number;
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    kind = Kind.Identifier;
                }
                else
                {
                    i += MultiCharOperators.Any(op => string.CompareOrdinal(s, i, op, 0, op.Length) == 0) ? 2 : 1;
                    kind = Kind.Operator;
                }
                tokens.Add(new SurfaceToken(kind, s.Substring(start, i - start)));
            }
            return tokens;
        }

        /// <summary>The end of the text literal starting at <paramref name="i"/>.</summary>
        private static int SkipText(string s, int i)
        {
            if (s[i] == '$') i++;
            if (string.CompareOrdinal(s, i, "\"\"\"", 0, 3) == 0)
            {
                // Raw: closes on the last three quotes of a run.
                var close = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                if (close < 0) return s.Length;
                var end = close + 3;
                while (end < s.Length && s[end] == '"') end++;
                return end;
            }

            i++;
            var braces = 0;
            while (i < s.Length && s[i] != '\n')
            {
                var c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '{') braces++;
                else if (c == '}' && braces > 0) braces--;
                else if (c == '"')
                {
                    if (braces == 0) return i + 1;
                    i = SkipText(s, i);   // a text inside an interpolation
                    continue;
                }
                i++;
            }
            return Math.Min(i, s.Length);
        }

        private static string Render(IEnumerable<SurfaceToken> tokens)
        {
            var sb = new StringBuilder();
            foreach (var t in tokens) sb.Append(t.Value);
            return sb.ToString();
        }

        private static List<string> Lines(string text) => text.Replace("\r", "").Split('\n').ToList();

        private static bool IsName(SurfaceToken t) => t.Kind == Kind.Identifier && !Reserved.Contains(t.Value);

        private static List<string> Names(List<SurfaceToken> tokens) =>
            tokens.Where(IsName).Select(t => t.Value).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        private static int PickIndex(List<SurfaceToken> tokens, Func<SurfaceToken, bool> where, Prng rng)
        {
            var candidates = new List<int>();
            for (var i = 0; i < tokens.Count; i++)
                if (where(tokens[i])) candidates.Add(i);
            return candidates.Count == 0 ? -1 : rng.Pick(candidates);
        }

        private static string Short(string s)
        {
            s = s.Replace("\n", "\\n");
            return s.Length <= 40 ? s : s.Substring(0, 37) + "...";
        }
    }
}
