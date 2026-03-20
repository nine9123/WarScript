using System.Collections.Generic;
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
        // TODO: Track age of cached tokens.
        // TODO: Have maximum amount of cached tokens.
        // TODO: Throw out oldest token list when limit is reached.
        /// <summary>
        /// Cache tokens generated from source code
        /// </summary>
        private static readonly Dictionary<string, List<Token.Token>> Cache = new();

        private readonly string _source;
        private readonly List<Token.Token> _tokens;
        private int _pos;
        private int _row;

        public static List<Token.Token> Parse(string sourceCode)
        {
            if (Cache.TryGetValue(sourceCode, out var cached))
                return cached;

            var parser = new LexicalParser(sourceCode);
            parser.Scan();

            Cache[sourceCode] = parser._tokens;
            return parser._tokens;
        }

        public static void ClearCache() => Cache.Clear();

        private LexicalParser(string source)
        {
            _source = source;
            _tokens = new List<Token.Token>();
            _pos = 0;
            _row = 1;
        }

        private char Current => _source[_pos];
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

            // ── Whitespace (skip, no token) ──
            if (c == ' ' || c == '\t')
            {
                _pos++;
                return;
            }

            // ── Line break ──
            if (c == '\n' || c == '\r')
            {
                _tokens.Add(new Token.Token(Token.TokenType.LineBreak, c.ToString(), _row));
                _row++;
                _pos++;
                return;
            }

            // ── Comment ──
            if (c == '#')
            {
                var start = _pos;
                while (!AtEnd && Current != '\n' && Current != '\r')
                    _pos++;
                _tokens.Add(new Token.Token(Token.TokenType.Comment, _source.Substring(start, _pos - start), _row));
                return;
            }

            // ── String literal ──
            if (c == '"')
            {
                ScanString();
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
                        _tokens.Add(new Token.Token(Token.TokenType.Operator, two, _row));
                        _pos += 2;
                        return;
                    case "..":
                        _tokens.Add(new Token.Token(Token.TokenType.GroupDivider, two, _row));
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
                            _tokens.Add(new Token.Token(Token.TokenType.Operator, full, _row));
                            _pos = rest + 3;
                        }
                        else
                        {
                            _tokens.Add(new Token.Token(Token.TokenType.Operator, "::", _row));
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
                    _tokens.Add(new Token.Token(Token.TokenType.Operator, c.ToString(), _row));
                    _pos++;
                    return;
            }

            // ── Group dividers ──
            switch (c)
            {
                case '[': case ']': case '{': case '}': case ',':
                    _tokens.Add(new Token.Token(Token.TokenType.GroupDivider, c.ToString(), _row));
                    _pos++;
                    return;
                case ':':
                    // Single colon (not ::, handled above)
                    _tokens.Add(new Token.Token(Token.TokenType.GroupDivider, ":", _row));
                    _pos++;
                    return;
            }

            // ── Identifiers, keywords, and word-operators ──
            if (IsIdentStart(c))
            {
                ScanIdentifier();
                return;
            }

            throw new SyntaxException($"Unexpected character '{c}' at line {_row}");
        }
        
        private void ScanString()
        {
            _pos++; // skip opening quote
            var start = _pos;
            var hasInterpolation = false;
            var isFirstSegment = true;

            while (!AtEnd && Current != '"')
            {
                if (Current == '{')
                {
                    hasInterpolation = true;

                    // Emit the text segment before the { (if non-empty)
                    if (_pos > start)
                    {
                        if (!isFirstSegment)
                            _tokens.Add(new Token.Token(TokenType.Operator, "+", _row));
                        _tokens.Add(new Token.Token(TokenType.Text, _source.Substring(start, _pos - start), _row));
                        isFirstSegment = false;
                    }

                    _pos++; // skip {

                    // Collect expression content, tracking brace depth
                    var exprStart = _pos;
                    var depth = 1;
                    while (!AtEnd && depth > 0)
                    {
                        if (Current == '{') depth++;
                        else if (Current == '}') depth--;
                        if (depth > 0) _pos++;
                    }

                    var exprSource = _source.Substring(exprStart, _pos - exprStart);
                    _pos++; // skip closing }

                    // Emit: + ( <expression tokens> )
                    if (!isFirstSegment)
                        _tokens.Add(new Token.Token(TokenType.Operator, "+", _row));
                    _tokens.Add(new Token.Token(TokenType.Operator, "(", _row));

                    // Recursively lex the expression
                    var innerParser = new LexicalParser(exprSource);
                    innerParser._row = _row;
                    innerParser.Scan();
                    foreach (var token in innerParser._tokens)
                        _tokens.Add(token);

                    _tokens.Add(new Token.Token(TokenType.Operator, ")", _row));
                    isFirstSegment = false;

                    // Next text segment starts after the }
                    start = _pos;
                }
                else
                {
                    _pos++;
                }
            }

            // Emit trailing text segment (or the whole string if no interpolation)
            if (!hasInterpolation)
            {
                _tokens.Add(new Token.Token(TokenType.Text, _source.Substring(start, _pos - start), _row));
            }
            else if (_pos > start)
            {
                _tokens.Add(new Token.Token(TokenType.Operator, "+", _row));
                _tokens.Add(new Token.Token(TokenType.Text, _source.Substring(start, _pos - start), _row));
            }

            _pos++; // skip closing quote
        }

        private void ScanNumber()
        {
            var start = _pos;

            // Optional leading minus
            if (Current == '-')
                _pos++;

            // Integer part
            while (!AtEnd && char.IsDigit(Current))
                _pos++;

            // Decimal part (but not .. range operator)
            if (!AtEnd && Current == '.' && Peek() != '.')
            {
                _pos++;
                while (!AtEnd && char.IsDigit(Current))
                    _pos++;
            }

            var value = _source.Substring(start, _pos - start);
            _tokens.Add(new Token.Token(Token.TokenType.Numeric, value, _row));
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
            _tokens.Add(new Token.Token(type, word, _row));
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
        /// Negative if preceded by: nothing, operator, group divider, or keyword.
        /// </summary>
        private bool IsNegativeSign()
        {
            if (_tokens.Count == 0)
                return true;

            var prev = _tokens[_tokens.Count - 1];
            return prev.Type == Token.TokenType.Operator
                || prev.Type == Token.TokenType.GroupDivider
                || prev.Type == Token.TokenType.Keyword
                || prev.Type == Token.TokenType.LineBreak;
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
