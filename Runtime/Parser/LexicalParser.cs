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

        /// <summary>
        /// For each <c>(</c> still open: whether it opened an argument or
        /// parameter list (a <see cref="TokenType.GroupDivider"/>) rather
        /// than a parenthesized expression (an <see cref="TokenType.Operator"/>),
        /// so its <c>)</c> gets the same type (T3.12).
        /// </summary>
        private readonly Stack<bool> _parens = new Stack<bool>();

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
                        throw new SyntaxException(
                            System.FormattableString.Invariant($"Unexpected '::' at line {_row}: members are written obj.name"), _row, col);
                }
            }

            // ── Single-character operators ──
            switch (c)
            {
                case '+': case '-': case '*': case '/': case '%':
                case '>': case '<': case '!': case '=':
                    Add(TokenType.Operator, c.ToString(), _row, col);
                    _pos++;
                    return;
                case '(':
                    OpenParen(col);
                    _pos++;
                    return;
                case ')':
                    CloseParen(col);
                    _pos++;
                    return;
                case '.':
                    // Not '..' (a range) nor '.5' (a number): both handled above.
                    AddMember(col);
                    return;
            }

            // ── Group dividers ──
            // '[' ']' are an array literal's or an index's; '{' '}' only
            // appear in text interpolation (T3.12).
            switch (c)
            {
                case ',':
                    Add(TokenType.GroupDivider, ",", _row, col);
                    _pos++;
                    return;
                case '[':
                    Add(TokenType.GroupDivider, "[", _row, col);
                    _pos++;
                    return;
                case ']':
                    Add(TokenType.GroupDivider, "]", _row, col);
                    _pos++;
                    return;
                case '{':
                case '}':
                    throw new SyntaxException(
                        System.FormattableString.Invariant($"Unexpected '{c}' at line {_row}: arrays are written [1, 2, 3] and indexed a[i]"), _row, col);
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

            // ── Field access: @name is `this.name` (T3.3, DEC-6) ──
            if (c == '@')
            {
                ScanField(col);
                return;
            }

            throw new SyntaxException(System.FormattableString.Invariant($"Unexpected character '{c}' at line {_row}"), _row, col);
        }

        /// <summary>
        /// A <c>(</c> right after a name or <c>fun</c> opens an argument or
        /// parameter list (<c>f(a)</c>, <c>fun f(a)</c>, <c>fun(x)</c>,
        /// <c>new P(1)</c>, <c>obj.m()</c>): a <see cref="TokenType.GroupDivider"/>.
        /// Any other one groups an expression: an <see cref="TokenType.Operator"/>
        /// (also after <c>yield wait</c> / <c>yield until</c>).
        /// A line break between the name and the <c>(</c> ends the statement
        /// instead (T3.12).
        /// </summary>
        private void OpenParen(int col)
        {
            var call = false;
            if (_tokens.Count > 0)
            {
                var prev = _tokens[_tokens.Count - 1];
                call = prev.Type == TokenType.Variable && !IsYieldWord(prev)
                    || prev.Type == TokenType.Keyword && prev.Value == "fun";
            }
            _parens.Push(call);
            Add(call ? TokenType.GroupDivider : TokenType.Operator, "(", _row, col);
        }

        /// <summary>
        /// <c>wait</c> or <c>until</c> right after <c>yield</c>: lexed as
        /// names (they are reserved nowhere else), but a <c>(</c> after them
        /// groups the duration or condition (<c>yield until (x > 0)</c>).
        /// </summary>
        private bool IsYieldWord(Token.Token word)
        {
            if (word.Value != "wait" && word.Value != "until" || _tokens.Count < 2) return false;
            var before = _tokens[_tokens.Count - 2];
            return before.Type == TokenType.Keyword && before.Value == "yield";
        }

        private void CloseParen(int col)
        {
            var call = _parens.Count > 0 && _parens.Pop();
            Add(call ? TokenType.GroupDivider : TokenType.Operator, ")", _row, col);
        }

        /// <summary>
        /// The member operator: <c>.</c>, or <c>.new</c> when the word
        /// <c>new</c> follows it: a nested class's instance.
        /// </summary>
        private void AddMember(int col)
        {
            var rest = _pos + 1;
            while (rest < _source.Length && (_source[rest] == ' ' || _source[rest] == '\t'))
                rest++;
            if (rest + 3 <= _source.Length
                && string.CompareOrdinal(_source, rest, "new", 0, 3) == 0
                && (rest + 3 >= _source.Length || !IsIdentChar(_source[rest + 3])))
            {
                Add(TokenType.Operator, ".new", _row, col);
                _pos = rest + 3;
            }
            else
            {
                Add(TokenType.Operator, ".", _row, col);
                _pos++;
            }
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
            var firstToken = _tokens.Count;
            var hasHole = false;

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
                    // A literal that starts with a hole starts with "" so the
                    // whole chain is text concatenation: "{a}{b}" with two
                    // numbers is "12", not 3 (T3.6, DEC-8: '+' only formats
                    // when an operand is text).
                    if (text.Length > 0 || !emittedAny)
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
                    hasHole = true;

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

            // An interpolation is one operand: ( "x" + (a) + "y" ), so
            // "x{a}" * 2 repeats the whole text and "{a}{b}"{0} indexes it
            // rather than binding to the last part (T3.6 review).
            if (hasHole)
            {
                _tokens.Insert(firstToken, new Token.Token(TokenType.Operator, "(", openRow, openCol));
                Add(TokenType.Operator, ")", _row, ColumnAt(_pos));
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
        /// closing brace. Braces nest (so <c>{arr[0]}</c> works) and braces
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

            // Decimal part (but not the .. range operator, nor a member: `5.x`)
            // — allow _ separators here too (`1._5`; `1.` is 1).
            if (!AtEnd && Current == '.' && Peek() != '.' && !StartsMember(_pos + 1))
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

        /// <summary>
        /// Scans <c>@name</c> into the tokens <c>this</c>, <c>.</c> and
        /// <c>name</c>, all at the column of the <c>@</c> (5.7), so errors
        /// about the access point at it. The name follows the <c>@</c>
        /// directly and must be an identifier, not a keyword or literal word.
        /// </summary>
        private void ScanField(int col)
        {
            var start = _pos + 1;
            var end = start;
            while (end < _source.Length && IsIdentChar(_source[end]))
                end++;
            var word = _source.Substring(start, end - start);
            if (word.Length == 0 || !IsIdentStart(word[0]) || ClassifyWord(word) != Token.TokenType.Variable)
                throw new SyntaxException(
                    System.FormattableString.Invariant($"'@' must be followed by a field name at line {_row}"), _row, col);

            Add(TokenType.This, "this", _row, col);
            Add(TokenType.Operator, ".", _row, col);
            Add(TokenType.Variable, word, _row, col);
            _pos = end;
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
                case "global": // T3.3 (PROP-10): reserved, never a name
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
        /// closing index/array bracket or argument list, after which `-` is
        /// subtraction (e.g. `(1+2)-3`, `arr[0]-1`, `f(x)-1`).
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
                    return prev.Value != ")" && prev.Value != "]";
                case Token.TokenType.Keyword:
                case Token.TokenType.LineBreak:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a name starts at <paramref name="i"/>, just after a number's
        /// '.': then the '.' is the member operator (<c>5.x</c>), not a
        /// decimal point. <c>_</c> followed by a digit is a separator (<c>1._5</c>).
        /// </summary>
        private bool StartsMember(int i)
        {
            if (i >= _source.Length || !IsIdentStart(_source[i])) return false;
            if (_source[i] != '_') return true;
            var j = i;
            while (j < _source.Length && _source[j] == '_') j++;
            return j >= _source.Length || !char.IsDigit(_source[j]);
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
