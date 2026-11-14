using System.Collections.Generic;
using System.Text;
using WarScript.Exception;
using WarScript.Token;

namespace WarScript
{
    /// <summary>
    /// Transforming the source code into tokens.
    /// <see cref="Token"/>
    /// <see cref="TokenType"/>
    /// </summary>
    public class LexicalParser
    {
        /// <summary>Entries the token cache holds before evicting the least recently used one.</summary>
        public const int CacheCapacity = 64;

        /// <summary>
        /// Cache of tokens generated from source code (T1.9, DEC-19): bounded,
        /// synchronized, shared by every instance.
        /// </summary>
        private static readonly TokenCache Cache = new TokenCache(CacheCapacity);

        private readonly string _source;
        private readonly List<Token.Token> _tokens;
        private int _pos;
        private int _row;

        /// <summary>
        /// Index of the first character of the current line (T3.1): the column
        /// of position <c>p</c> is <c>p - _lineStart + 1</c>. An interpolation's
        /// lexer starts it below 0, so its tokens get their columns in the
        /// enclosing source.
        /// </summary>
        private int _lineStart;

        /// <summary>
        /// The tokens of <paramref name="sourceCode"/>, from the cache when the
        /// same source was lexed recently. The list is shared and read-only.
        /// Safe to call from several threads.
        /// </summary>
        public static IReadOnlyList<Token.Token> Parse(string sourceCode)
        {
            if (Cache.TryGet(sourceCode, out var cached))
                return cached;

            // Lex outside the lock: a SyntaxException caches nothing, and
            // threads lexing different sources don't wait for each other.
            var parser = new LexicalParser(sourceCode);
            parser.Scan();

            return Cache.Add(sourceCode, parser._tokens.AsReadOnly());
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>Number of sources in the token cache (tests).</summary>
        internal static int CachedCount => Cache.Count;

        private LexicalParser(string source)
        {
            _source = source;
            _tokens = new List<Token.Token>();
            _pos = 0;
            _row = 1;
        }

        private char Current => _source[_pos];

        /// <summary>Column of <paramref name="position"/> on the current line, from 1; a tab is one column.</summary>
        private int ColumnAt(int position) => position - _lineStart + 1;

        /// <summary>Count a line break that ends just before <paramref name="nextLineStart"/>.</summary>
        private void NewLine(int nextLineStart)
        {
            _row++;
            _lineStart = nextLineStart;
        }

        /// <summary>
        /// Whether the character at <paramref name="i"/> ends a line: a '\n',
        /// or a '\r' not followed by '\n' ("\r\n" ends at its '\n'). For the
        /// line breaks inside literals and interpolations, counted the way
        /// <see cref="ScanToken"/> counts the others.
        /// </summary>
        private bool EndsLine(int i) =>
            _source[i] == '\n' || (_source[i] == '\r' && (i + 1 >= _source.Length || _source[i + 1] != '\n'));

        private void Add(TokenType type, string value, int row, int column) =>
            _tokens.Add(new Token.Token(type, value, row, column));

        private bool AtEnd => _pos >= _source.Length;

        private char Peek(int offset = 1)
        {
            var idx = _pos + offset;
            return idx < _source.Length ? _source[idx] : '\0';
        }

        private void Scan()
        {
            while (!AtEnd)
            {
                ScanToken();
            }
        }

        private void ScanToken()
        {
            var c = Current;
            var col = ColumnAt(_pos);

            // ── Whitespace (skip, no token) ──
            if (c == ' ' || c == '\t')
            {
                _pos++;
                return;
            }

            // ── Line break ──
            // "\r\n" is one line break, as are a lone '\n' and a lone '\r'.
            if (c == '\n' || c == '\r')
            {
                var length = c == '\r' && Peek() == '\n' ? 2 : 1;
                Add(TokenType.LineBreak, _source.Substring(_pos, length), _row, col);
                _pos += length;
                NewLine(_pos);
                return;
            }

            // ── Comment ──
            if (c == '#')
            {
                var start = _pos;
                while (!AtEnd && Current != '\n' && Current != '\r')
                    _pos++;
                Add(TokenType.Comment, _source.Substring(start, _pos - start), _row, col);
                return;
            }

            // ── String literal ──
            if (c == '"')
            {
                // """ ... """ is a raw literal: no escapes, no interpolation.
                if (Peek(1) == '"' && Peek(2) == '"')
                    ScanRawString();
                else
                    ScanString(col);
                return;
            }

            // ── Explicitly interpolated string: $"..." ──
            if (c == '$' && Peek() == '"')
            {
                if (Peek(2) == '"' && Peek(3) == '"')
                    throw new SyntaxException(
                        System.FormattableString.Invariant($"Raw text literals (\"\"\") do not support interpolation at line {_row}"), _row, col);

                _pos++; // skip $
                ScanString(col);
                return;
            }

            // ── Numeric (including negatives when appropriate) ──
            if (char.IsDigit(c) || (c == '-' && !AtEnd && char.IsDigit(Peek()) && IsNegativeSign()))
            {
                ScanNumber();
                return;
            }

            // ── Decimal starting with . ──
            if (c == '.' && Peek() != '.' && char.IsDigit(Peek()))
            {
                ScanNumber();
                return;
            }

            // ── Two-character operators (check before single-char) ──
            if (_pos + 1 < _source.Length)
            {
                var two = _source.Substring(_pos, 2);
                switch (two)
                {
                    case "==":
                    case "!=":
                    case ">=":
                    case "<=":
                    case "<<":
                    case "+=":
                    case "-=":
                    case "*=":
                    case "/=":
                        Add(TokenType.Operator, two, _row, col);
                        _pos += 2;
                        return;
                    case "..":
                        Add(TokenType.GroupDivider, two, _row, col);
                        _pos += 2;
                        return;
                    case "::":
                        // Check for :: new
                        var rest = _pos + 2;
                        while (rest < _source.Length && (_source[rest] == ' ' || _source[rest] == '\t'))
                            rest++;
                        if (rest + 3 <= _source.Length
                            && _source.Substring(rest, 3) == "new"
                            && (rest + 3 >= _source.Length || !IsIdentChar(_source[rest + 3])))
                        {
                            var full = _source.Substring(_pos, rest + 3 - _pos);
                            Add(TokenType.Operator, full, _row, col);
                            _pos = rest + 3;
                        }
                        else
                        {
                            Add(TokenType.Operator, "::", _row, col);
                            _pos += 2;
                        }
                        return;
                }
            }

            // ── Single-character operators ──
            switch (c)
            {
                case '+': case '-': case '*': case '/': case '%':
                case '>': case '<': case '!': case '=':
                case '(': case ')':
                    Add(TokenType.Operator, c.ToString(), _row, col);
                    _pos++;
                    return;
            }

            // ── Group dividers ──
            switch (c)
            {
                case '[': case ']': case '{': case '}': case ',':
                    Add(TokenType.GroupDivider, c.ToString(), _row, col);
                    _pos++;
                    return;
                case ':':
                    // Single colon (not ::, handled above)
                    Add(TokenType.GroupDivider, ":", _row, col);
                    _pos++;
                    return;
            }

            // ── Identifiers, keywords, and word-operators ──
            if (IsIdentStart(c))
            {
                ScanIdentifier();
                return;
            }

            throw new SyntaxException(System.FormattableString.Invariant($"Unexpected character '{c}' at line {_row}"), _row, col);
        }
        
        /// <summary>
        /// Scans a regular text literal: <c>"..."</c> or <c>$"..."</c>.
        /// Supports <c>{expr}</c> interpolation and backslash escapes.
        /// The opening quote is expected at the current position;
        /// <paramref name="openCol"/> is the literal's column (its <c>$</c>
        /// when there is one), which its first text segment carries.
        /// </summary>
        private void ScanString(int openCol)
        {
            var openRow = _row;
            _pos++; // skip opening quote

            // Text of the current segment. The builder stays null while the
            // segment is a plain run of source characters, so an escape-free
            // literal costs exactly one Substring — same as before.
            var segStart = _pos;
            var segRow = _row;
            var segCol = openCol;
            StringBuilder segment = null;
            var emittedAny = false;
            var terminated = false;

            while (!AtEnd)
            {
                var c = Current;

                if (c == '"')
                {
                    terminated = true;
                    break;
                }

                if (c == '\\')
                {
                    segment ??= new StringBuilder();
                    segment.Append(_source, segStart, _pos - segStart);
                    var escapeCol = ColumnAt(_pos);
                    _pos++; // skip backslash
                    segment.Append(ReadEscape(escapeCol));
                    segStart = _pos;
                    continue;
                }

                if (c == '{')
                {
                    // Emit the text segment before the { (if non-empty)
                    var text = TakeSegment(ref segment, ref segStart);
                    if (text.Length > 0)
                    {
                        if (emittedAny)
                            Add(TokenType.Operator, "+", segRow, segCol);
                        Add(TokenType.Text, text, segRow, segCol);
                        emittedAny = true;
                    }

                    // The tokens desugared around the hole carry the column
                    // of its '{'; the expression's own tokens keep theirs.
                    var exprRow = _row;
                    var holeCol = ColumnAt(_pos);
                    var exprLineStart = _lineStart;
                    _pos++; // skip {
                    var exprStart = _pos;
                    var exprSource = ReadInterpolatedExpression(openRow, openCol);

                    // Emit: + ( <expression tokens> )
                    if (emittedAny)
                        Add(TokenType.Operator, "+", exprRow, holeCol);
                    Add(TokenType.Operator, "(", exprRow, holeCol);

                    // Recursively lex the expression
                    var innerParser = new LexicalParser(exprSource);
                    innerParser._row = exprRow;
                    innerParser._lineStart = exprLineStart - exprStart;
                    innerParser.Scan();
                    foreach (var token in innerParser._tokens)
                        _tokens.Add(token);

                    Add(TokenType.Operator, ")", exprRow, holeCol);
                    emittedAny = true;

                    // Next text segment starts after the }
                    segStart = _pos;
                    segRow = _row;
                    segCol = ColumnAt(_pos);
                    continue;
                }

                // A raw line break inside a literal is allowed, but the row
                // counter has to follow it or every later error points at the
                // wrong line.
                var endsLine = EndsLine(_pos);
                _pos++;
                if (endsLine)
                    NewLine(_pos);
            }

            if (!terminated)
                throw new SyntaxException(System.FormattableString.Invariant($"Unterminated text literal starting at line {openRow}"), openRow, openCol);

            // Emit the trailing segment — or the whole (possibly empty) literal
            // when there was no interpolation at all.
            var tail = TakeSegment(ref segment, ref segStart);
            if (tail.Length > 0 || !emittedAny)
            {
                if (emittedAny)
                    Add(TokenType.Operator, "+", segRow, segCol);
                Add(TokenType.Text, tail, segRow, segCol);
            }

            _pos++; // skip closing quote
        }

        /// <summary>
        /// Scans a raw text literal: <c>"""..."""</c>. Nothing inside is
        /// interpreted — no escapes, no interpolation — so it can carry
        /// WarScript source verbatim, quotes and braces included. A line break
        /// directly after the opening delimiter, and one directly before the
        /// closing delimiter, are dropped so a block reads as its lines and
        /// nothing else.
        /// </summary>
        private void ScanRawString()
        {
            var openRow = _row;
            var openCol = ColumnAt(_pos);
            _pos += 3; // skip opening """

            var end = FindRawStringEnd(_pos);
            if (end < 0)
                throw new SyntaxException(System.FormattableString.Invariant($"Unterminated raw text literal starting at line {openRow}"), openRow, openCol);

            var start = _pos;
            var stop = end;

            // Drop the line break that follows the opening delimiter.
            if (start < stop && _source[start] == '\r' && start + 1 < stop && _source[start + 1] == '\n')
                start += 2;
            else if (start < stop && (_source[start] == '\n' || _source[start] == '\r'))
                start += 1;

            // Drop the final line break plus the indentation of the closing
            // delimiter, so `"""` may sit on its own line without adding one.
            // The floor is the *untrimmed* start, so a block whose only content
            // is that one line break comes out empty rather than as indentation.
            var trimmed = stop;
            while (trimmed > _pos && (_source[trimmed - 1] == ' ' || _source[trimmed - 1] == '\t'))
                trimmed--;
            if (trimmed > _pos && (_source[trimmed - 1] == '\n' || _source[trimmed - 1] == '\r'))
            {
                trimmed--;
                if (_source[trimmed] == '\n' && trimmed > _pos && _source[trimmed - 1] == '\r')
                    trimmed--;
                stop = trimmed;
            }

            var value = stop > start ? _source.Substring(start, stop - start) : string.Empty;
            Add(TokenType.Text, value, openRow, openCol);

            // The literal may span lines; keep the row counter in step.
            for (var i = _pos; i < end; i++)
                if (EndsLine(i))
                    NewLine(i + 1);

            _pos = end + 3; // skip closing """
        }

        /// <summary>
        /// Finds where the content of a raw literal ends, searching from the
        /// first character after the opening delimiter. Returns -1 if the
        /// literal is never closed.
        ///
        /// A run of four or more quotes closes with its *last* three, so
        /// content may end in a quote: <c>"""say "hi""""</c> is <c>say "hi"</c>.
        /// Content containing a run of three quotes therefore cannot be
        /// written raw.
        /// </summary>
        private int FindRawStringEnd(int from)
        {
            for (var i = from; i + 2 < _source.Length; i++)
            {
                if (_source[i] != '"' || _source[i + 1] != '"' || _source[i + 2] != '"')
                    continue;

                var runEnd = i + 3;
                while (runEnd < _source.Length && _source[runEnd] == '"')
                    runEnd++;

                return runEnd - 3;
            }

            return -1;
        }

        /// <summary>
        /// Returns the text accumulated for the current segment and resets it.
        /// </summary>
        private string TakeSegment(ref StringBuilder segment, ref int segStart)
        {
            string text;
            if (segment == null)
            {
                text = _pos > segStart ? _source.Substring(segStart, _pos - segStart) : string.Empty;
            }
            else
            {
                segment.Append(_source, segStart, _pos - segStart);
                text = segment.ToString();
                segment = null;
            }

            segStart = _pos;
            return text;
        }

        /// <summary>
        /// Reads the character named by an escape sequence. The backslash has
        /// already been consumed; the position is left after the sequence.
        /// </summary>
        private char ReadEscape(int escapeCol)
        {
            if (AtEnd)
                throw new SyntaxException(System.FormattableString.Invariant($"Text literal ends with a dangling '\\' at line {_row}"), _row, escapeCol);

            var c = Current;
            _pos++;

            switch (c)
            {
                case '"': return '"';
                case '\\': return '\\';
                case '{': return '{';
                case '}': return '}';
                case 'n': return '\n';
                case 't': return '\t';
                case 'r': return '\r';
                default:
                    throw new SyntaxException(System.FormattableString.Invariant($"Unknown escape sequence '\\{c}' at line {_row}"), _row, escapeCol);
            }
        }

        /// <summary>
        /// Reads the source of a <c>{...}</c> interpolation, starting just
        /// after the opening brace and leaving the position after the matching
        /// closing brace. Braces nest (so <c>{arr{0}}</c> works) and braces
        /// inside a nested text literal are ignored.
        /// </summary>
        private string ReadInterpolatedExpression(int openRow, int openCol)
        {
            var start = _pos;
            var depth = 1;

            while (!AtEnd)
            {
                var c = Current;

                if (c == '"')
                {
                    SkipNestedLiteral();
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var source = _source.Substring(start, _pos - start);
                        _pos++; // skip closing }
                        return source;
                    }
                }
                else if (EndsLine(_pos))
                {
                    NewLine(_pos + 1);
                }

                _pos++;
            }

            throw new SyntaxException(System.FormattableString.Invariant($"Unterminated interpolation in text literal at line {openRow}"), openRow, openCol);
        }

        /// <summary>
        /// Advances past a text literal nested inside an interpolation, so its
        /// braces and escaped quotes are not mistaken for expression syntax.
        /// The opening quote is expected at the current position.
        /// </summary>
        private void SkipNestedLiteral()
        {
            var start = _pos;
            int stop;

            if (Peek(1) == '"' && Peek(2) == '"')
            {
                var end = FindRawStringEnd(_pos + 3);
                stop = end < 0 ? _source.Length : end + 3;
            }
            else
            {
                stop = _pos + 1; // skip opening quote
                while (stop < _source.Length)
                {
                    if (_source[stop] == '\\')
                    {
                        stop += 2;
                        continue;
                    }

                    stop++;
                    if (_source[stop - 1] == '"')
                        break;
                }

                if (stop > _source.Length)
                    stop = _source.Length;
            }

            // The nested literal may span lines; keep the row counter in step.
            for (var i = start; i < stop; i++)
                if (EndsLine(i))
                    NewLine(i + 1);

            _pos = stop;
        }

        private void ScanNumber()
        {
            var start = _pos;

            // Optional leading minus
            if (Current == '-')
                _pos++;

            // Integer part — allow _ separators between digits (e.g. 1_000_000)
            while (!AtEnd && (char.IsDigit(Current) || Current == '_'))
                _pos++;

            // Decimal part (but not .. range operator) — allow _ separators here too
            if (!AtEnd && Current == '.' && Peek() != '.')
            {
                _pos++;
                while (!AtEnd && (char.IsDigit(Current) || Current == '_'))
                    _pos++;
            }

            // Strip underscores before parsing — they are purely visual separators
            var raw = _source.Substring(start, _pos - start);
            var value = raw.Replace("_", "");
            Add(TokenType.Numeric, value, _row, ColumnAt(start));
        }

        private void ScanIdentifier()
        {
            var start = _pos;
            while (!AtEnd && IsIdentChar(Current))
                _pos++;

            var word = _source.Substring(start, _pos - start);

            // Classify the word — no lookahead needed.
            // If we got here, we already know the next char is NOT
            // alphanumeric/underscore, so "this_thing" is one token,
            // and "this]" correctly splits into "this" + "]".
            var type = ClassifyWord(word);
            Add(type, word, _row, ColumnAt(start));
        }

        private static Token.TokenType ClassifyWord(string word)
        {
            switch (word)
            {
                // Keywords
                case "if": case "elif": case "else": case "end":
                case "print": case "class": case "fun": case "return":
                case "loop": case "in": case "by": case "break":
                case "next": case "assert": case "raise":
                case "begin": case "rescue": case "ensure":
                case "import":
                case "yield":
                case "const": case "enum":
                    return Token.TokenType.Keyword;

                // Word-operators
                case "new": case "and": case "or": case "as": case "is":
                    return Token.TokenType.Operator;

                // Literals
                case "true": case "false":
                    return Token.TokenType.Logical;

                case "null":
                    return Token.TokenType.Null;

                case "this":
                    return Token.TokenType.This;

                // Everything else
                default:
                    return Token.TokenType.Variable;
            }
        }

        /// <summary>
        /// Determines if a minus sign should be treated as a negative number
        /// rather than a subtraction operator.
        /// Negative if preceded by: nothing, operator, group divider, or keyword —
        /// except tokens that themselves end an operand: a closing paren, or a
        /// closing array/argument bracket, after which `-` is subtraction
        /// (e.g. `(1+2)-3`, `arr{0}-1`, `f[x]-1`).
        /// </summary>
        private bool IsNegativeSign()
        {
            if (_tokens.Count == 0)
                return true;

            var prev = _tokens[_tokens.Count - 1];
            switch (prev.Type)
            {
                case Token.TokenType.Operator:
                    return prev.Value != ")";
                case Token.TokenType.GroupDivider:
                    return prev.Value != "}" && prev.Value != "]";
                case Token.TokenType.Keyword:
                case Token.TokenType.LineBreak:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIdentStart(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || c == '_';

        private static bool IsIdentChar(char c)
            => IsIdentStart(c)
            || (c >= '0' && c <= '9');
    }
}
