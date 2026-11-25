// SyntaxRewriter: rewrites a script from the legacy syntax to the current one
// (T3.12):
//
//   f [a, b]       -> f(a, b)        fun f [a]  -> fun f(a)    fun [x] -> fun(x)
//   class C [x] : B [x] -> class C(x) : B(x)                   new C [1] -> new C(1)
//   {1, 2}         -> [1, 2]         a{i}       -> a[i]
//   obj :: x       -> obj.x          obj :: m [] -> obj.m()    obj :: new C [] -> obj.new C()
//
// It works on the legacy lexer's tokens and edits only those characters (and
// the blanks the old style put before a call's '[' and around '::'), so
// comments, layout and everything else stay as written. The code inside an
// interpolation ("{arr{0}}") is rewritten too. A raw literal ("""...""") is
// left alone: when it holds script source, rewrite that by hand; the result
// lists every one.

using System.Collections.Generic;
using System.Text;
using WarScript.Token;

namespace WarScript.Migrate
{
    public static class SyntaxRewriter
    {
        public sealed class Result
        {
            /// <summary>The rewritten source (the input when nothing changed).</summary>
            public string Source;

            /// <summary>Number of tokens rewritten.</summary>
            public int Edits;

            /// <summary>Lines (from 1) of the raw text literals, which are not rewritten.</summary>
            public readonly List<int> RawLiteralLines = new List<int>();
        }

        /// <summary>The legacy lexer's tokens of <paramref name="source"/>.</summary>
        public static IReadOnlyList<Token.Token> LegacyTokens(string source) => LexicalParser.ParseLegacy(source);

        /// <summary>
        /// Rewrites <paramref name="source"/>, written in the legacy syntax.
        /// Throws the lexer's <see cref="Exception.SyntaxException"/> when it
        /// does not lex as legacy WarScript, and a <see cref="Exception.SyntaxException"/>
        /// when it has a form only the current syntax writes (<see cref="RejectCurrentSyntax"/>):
        /// a file rewritten already would otherwise be rewritten again, its
        /// arrays and indexes turned into calls.
        /// </summary>
        public static Result Rewrite(string source)
        {
            var tokens = LexicalParser.ParseLegacy(source);
            var lineStarts = LineStarts(source);
            RejectCurrentSyntax(source, tokens, lineStarts);
            var edits = new List<(int Start, int End, string Text)>();
            var result = new Result();

            int Offset(Token.Token t) =>
                t.RowNumber >= 1 && t.RowNumber <= lineStarts.Count && t.Column >= 1
                    ? lineStarts[t.RowNumber - 1] + t.Column - 1
                    : -1;

            // Blanks (spaces, tabs) just before / after a position, on its line.
            int BlankBefore(int at)
            {
                var i = at;
                while (i > 0 && (source[i - 1] == ' ' || source[i - 1] == '\t')) i--;
                // Keep a line's indentation.
                return i == 0 || source[i - 1] == '\n' || source[i - 1] == '\r' ? at : i;
            }
            int BlankAfter(int at)
            {
                var i = at;
                while (i < source.Length && (source[i] == ' ' || source[i] == '\t')) i++;
                return i;
            }

            foreach (var t in tokens)
            {
                var at = Offset(t);
                if (at < 0 || at >= source.Length) continue;
                var c = source[at];

                if (t.Type == TokenType.Text && c == '"' && at + 2 < source.Length && source[at + 1] == '"' && source[at + 2] == '"')
                {
                    result.RawLiteralLines.Add(t.RowNumber);
                    continue;
                }

                if (t.Type == TokenType.GroupDivider)
                {
                    // Tokens desugared around an interpolation sit on its quote
                    // or '{': only the ones on their own character are edited.
                    if (t.Value == "(" && c == '[')
                        edits.Add((BlankBefore(at), at + 1, "("));
                    else if (t.Value == ")" && c == ']')
                        edits.Add((at, at + 1, ")"));
                    else if (t.Value == "[" && c == '{')
                        edits.Add((at, at + 1, "["));
                    else if (t.Value == "]" && c == '}')
                        edits.Add((at, at + 1, "]"));
                }
                else if (t.Type == TokenType.Operator && (t.Value == "." || t.Value == ".new")
                         && c == ':' && at + 1 < source.Length && source[at + 1] == ':')
                {
                    // `a :: b` -> `a.b`; `a :: new C` -> `a.new C` (the lexer
                    // took `new` into the token; it stays as written).
                    edits.Add((BlankBefore(at), BlankAfter(at + 2), "."));
                }
            }

            result.Edits = edits.Count;
            if (edits.Count == 0)
            {
                result.Source = source;
                return result;
            }

            edits.Sort((a, b) => a.Start.CompareTo(b.Start));
            var sb = new StringBuilder(source.Length);
            var pos = 0;
            foreach (var (start, end, text) in edits)
            {
                // Blanks before a '[' may already have been taken by an edit
                // on their left (`a :: m [x]` takes none: the blanks after
                // '::' stop at `m`), so overlaps cannot happen; stay safe anyway.
                var from = start < pos ? pos : start;
                sb.Append(source, pos, from - pos);
                sb.Append(text);
                pos = end < from ? from : end;
            }
            sb.Append(source, pos, source.Length - pos);
            result.Source = sb.ToString();
            return result;
        }

        /// <summary>
        /// Throws when <paramref name="source"/> has a form that the current
        /// syntax writes and the legacy one does not, or reads differently:
        /// a '[' that is not right after a name or <c>fun</c> (an array
        /// literal; the legacy syntax writes arrays <c>{1, 2}</c> and a '['
        /// only opens a call's arguments), or a '(' right after a name on its
        /// line (a call now, a group in the legacy syntax; such a line is
        /// rewritten by hand), or a name's '[...]' followed by an assignment
        /// (an index write; the legacy syntax cannot assign to a call). A
        /// rewritten file that has none of these (nor a '.' member, which the
        /// legacy lexer already rejects) only reads indexes, and is rewritten
        /// again: <c>a[0]</c> becomes <c>a(0)</c>.
        /// </summary>
        private static void RejectCurrentSyntax(string source, IReadOnlyList<Token.Token> legacy, List<int> lineStarts)
        {
            int Offset(Token.Token t) =>
                t.RowNumber >= 1 && t.RowNumber <= lineStarts.Count && t.Column >= 1
                    ? lineStarts[t.RowNumber - 1] + t.Column - 1
                    : -1;
            bool On(Token.Token t, char c)
            {
                var at = Offset(t);
                return at >= 0 && at < source.Length && source[at] == c;
            }

            for (var i = 0; i < legacy.Count; i++)
            {
                var t = legacy[i];
                if (t.Type != TokenType.GroupDivider || t.Value != "(" || !On(t, '[')) continue;
                if (i > 0 && (legacy[i - 1].Type == TokenType.Variable
                              || (legacy[i - 1].Type == TokenType.Keyword && legacy[i - 1].Value == "fun")))
                {
                    // `s[0] = x`: the legacy syntax cannot assign to a call.
                    var close = MatchingClose(legacy, i);
                    if (close + 1 < legacy.Count && IsAssignment(legacy[close + 1]))
                        throw new Exception.SyntaxException(
                            "'[' after a name followed by an assignment is an index write in the current syntax "
                            + "(the legacy syntax writes a{i} = x): the file looks rewritten already",
                            t.RowNumber, t.Column);
                    continue;
                }
                throw new Exception.SyntaxException(
                    "'[' starts an array literal, which the legacy syntax writes {1, 2}: the file looks rewritten already",
                    t.RowNumber, t.Column);
            }

            IReadOnlyList<Token.Token> current;
            try { current = LexicalParser.Parse(source); }
            catch (Exception.SyntaxException) { return; } // '{' or '::': legacy only
            foreach (var t in current)
            {
                if (t.Type != TokenType.GroupDivider || t.Value != "(" || !On(t, '(')) continue;
                throw new Exception.SyntaxException(
                    "'(' right after a name is a call in the current syntax but a group in the legacy one: "
                    + "the file looks rewritten already; if not, rewrite this line by hand",
                    t.RowNumber, t.Column);
            }
        }

        /// <summary>The index of the ')' closing the '(' at <paramref name="open"/>, or the token count.</summary>
        private static int MatchingClose(IReadOnlyList<Token.Token> tokens, int open)
        {
            var depth = 0;
            for (var i = open; i < tokens.Count; i++)
            {
                if (tokens[i].Type != TokenType.GroupDivider) continue;
                if (tokens[i].Value == "(") depth++;
                else if (tokens[i].Value == ")" && --depth == 0) return i;
            }
            return tokens.Count;
        }

        private static bool IsAssignment(Token.Token t) =>
            t.Type == TokenType.Operator
            && (t.Value == "=" || t.Value == "+=" || t.Value == "-=" || t.Value == "*=" || t.Value == "/=");

        /// <summary>
        /// The offset of each line's first character, with the lexer's line
        /// breaks: "\r\n", a lone "\n" and a lone "\r".
        /// </summary>
        private static List<int> LineStarts(string source)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n') continue;
                if (source[i] == '\n' || source[i] == '\r') starts.Add(i + 1);
            }
            return starts;
        }
    }
}
