using System.Collections.Generic;
using WarScript.Exception;

namespace WarScript.Token
{
    public class TokensStack
    {
        private readonly List<Token> _tokens;
        private int _position;

        // LineBreak and Comment are "empty" tokens skipped between meaningful lexemes.
        // Skipping runs on every Next/Peek/HasNext, so this is extremely hot: a direct
        // comparison beats a HashSet<enum> lookup, which on Unity's Mono routes through
        // EnumEqualityComparer / JitHelpers.UnsafeEnumCast on each Contains() call.
        private static bool IsEmptyToken(TokenType type) =>
            type == TokenType.LineBreak || type == TokenType.Comment;

        public TokensStack(List<Token> tokens)
        {
            _tokens = tokens;
        }

        // Advance and return token matching any of the given types
        public Token Next(TokenType type, params TokenType[] types)
        {
            SkipEmptyTokens();
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (token.Type == type)
                {
                    _position++;
                    return token;
                }
                for (var i = 0; i < types.Length; i++)
                {
                    if (types[i] == token.Type)
                    {
                        _position++;
                        return token;
                    }
                }
            }
            throw new SyntaxException($"After `{Previous()}` declaration expected any of the following lexemes `{type}, {string.Join(", ", types)}`");
        }

        /// <summary>
        /// Advance and return token matching the given type and value
        /// </summary>
        public Token Next(TokenType type, string value, params string[] values)
        {
            SkipEmptyTokens();
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (token.Type == type)
                {
                    if (token.Value == value)
                    {
                        _position++;
                        return token;
                    }
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (values[i] == token.Value)
                        {
                            _position++;
                            return token;
                        }
                    }
                }
            }
            throw new SyntaxException($"After `{Previous()}` declaration expected `{type}, {value}` lexeme");
        }

        // Advance and return the next non-empty token unconditionally
        public Token Next()
        {
            SkipEmptyTokens();
            return _tokens[_position++];
        }

        public void Back()
        {
            _position--;
        }

        public bool HasNext()
        {
            SkipEmptyTokens();
            return _position < _tokens.Count;
        }

        // Peek skipping empty tokens (LineBreak, Comment)
        public bool Peek(TokenType type, string value, params string[] values)
        {
            SkipEmptyTokens();
            return PeekSameLine(type, value, values);
        }

        // Peek without skipping empty tokens
        public bool PeekSameLine(TokenType type, string value, params string[] values)
        {
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (token.Type != type)
                    return false;
                if (token.Value == value)
                    return true;
                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i] == token.Value)
                        return true;
                }
            }
            return false;
        }

        // Peek skipping empty tokens (LineBreak, Comment)
        public bool Peek(TokenType type, params TokenType[] types)
        {
            SkipEmptyTokens();
            return PeekSameLine(type, types);
        }

        // Peek without skipping empty tokens
        public bool PeekSameLine(TokenType type, params TokenType[] types)
        {
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (token.Type == type)
                    return true;
                for (var i = 0; i < types.Length; i++)
                {
                    if (types[i] == token.Type)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Peek the next token on the current line WITHOUT skipping empty tokens and
        /// without allocating. Lets a caller inspect the token's type/value directly
        /// instead of making several PeekSameLine calls (each building a params array).
        /// </summary>
        public bool TryPeekSameLine(out Token token)
        {
            if (_position < _tokens.Count)
            {
                token = _tokens[_position];
                return true;
            }
            token = default;
            return false;
        }

        /// <summary>
        /// Skip empty tokens (LineBreak, Comment), then peek the next token without allocating.
        /// </summary>
        public bool TryPeek(out Token token)
        {
            SkipEmptyTokens();
            return TryPeekSameLine(out token);
        }

        private Token Previous() => _tokens[_position - 1];

        private void SkipEmptyTokens()
        {
            while (_position < _tokens.Count && IsEmptyToken(_tokens[_position].Type))
                _position++;
        }
    }
}
