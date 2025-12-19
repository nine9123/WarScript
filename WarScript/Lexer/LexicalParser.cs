#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WarScript.Lexer.Extensions;

namespace WarScript.Lexer
{
    public class LexicalParser
    {
        private readonly string _sourceCode;

        public LexicalParser(string sourceCode)
        {
            _sourceCode = sourceCode;
        }
        
        public List<Token> Parse()
        {
            var tokens = new List<Token>();

            var currentLine = 1;
            var position = 0;
            
            while (position < _sourceCode.Length)
            {
                var (token, length, newLine) = NextToken(_sourceCode, position, currentLine);
                
                if (token != null)
                    tokens.Add(token);

                currentLine = newLine;
                position += length;
            }

            return tokens;
        }

        private (Token?, int, int) NextToken(string sourceCode, int position, int currentLine)
        {
            var nextToken = sourceCode.Substring(position);
            
            var tokenTypes = (TokenType[])Enum.GetValues(typeof(TokenType));
            foreach (var tokenType in tokenTypes)
            {
                var pattern = $"^{tokenType.GetRegex()}";
                var match = Regex.Match(nextToken, pattern);

                if (match.Success)
                {
                    Token? token = null;
                    
                    // Custom logic: Count on which line the token is
                    if (tokenType == TokenType.LineBreak)
                        currentLine++;
                    
                    // Ignore whitespace, only used to divide two lexemes
                    if (tokenType != TokenType.Whitespace)
                    {
                        var tokenValue = match.Groups.Count > 1
                            ? match.Groups[1].Value // This gets the text literal without double quotes
                            : match.Value;

                        token = new Token(tokenType, tokenValue, currentLine);
                    }
                    
                    return (token, match.Value.Length, currentLine);
                }
            }
            
            throw new Exception($"Invalid expression: {nextToken}");
        }
    }
}