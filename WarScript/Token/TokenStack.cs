using System;
using System.Collections.Generic;
using System.Linq;
using WarScript.Exception;

namespace WarScript.Token
{
    public class TokensStack
    {
        private readonly List<Token> _tokens;
        private int _position;

        private static readonly HashSet<TokenType> EmptyTokens = new HashSet<TokenType>()
        {
            TokenType.LineBreak,
            TokenType.Comment
        };

        public TokensStack(List<Token> tokens)
        {
            _tokens = tokens;
        }

        // Advance and return token matching any of the given types
        public Token Next(TokenType type, params TokenType[] types)
        {
            SkipEmptyTokens();
            var tokenTypes = types.Append(type);
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (tokenTypes.Any(t => t == token.Type))
                {
                    _position++;
                    return token;
                }
            }
            throw new SyntaxException($"After `{Previous()}` declaration expected any of the following lexemes `{string.Join(", ", types)}`");
        }

        // Advance and return token matching the given type and value
        public Token Next(TokenType type, string value, params string[] values)
        {
            SkipEmptyTokens();
            if (_position < _tokens.Count)
            {
                var allValues = values.Append(value);
                var token = _tokens[_position];
                if (token.Type == type && allValues.Any(v => v == token.Value))
                {
                    _position++;
                    return token;
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
                var allValues = values.Append(value);
                var token = _tokens[_position];
                return token.Type == type && allValues.Any(v => v == token.Value);
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
                var tokenTypes = types.Append(type);
                var token = _tokens[_position];
                return tokenTypes.Any(t => t == token.Type);
            }
            return false;
        }

        private Token Previous() => _tokens[_position - 1];

        private void SkipEmptyTokens()
        {
            while (_position < _tokens.Count && EmptyTokens.Contains(_tokens[_position].Type))
                _position++;
        }
    }
}