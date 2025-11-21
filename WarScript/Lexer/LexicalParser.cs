#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WarScript.Lexer.Extensions;

namespace WarScript.Lexer
{
    public class LexicalParser
    {
        private readonly string[] _sourceCode;

        public LexicalParser(string[] sourceCode)
        {
            _sourceCode = sourceCode;
        }
        
        public List<Token> Parse()
        {
            var tokens = new List<Token>();

            for (var line = 0; line < _sourceCode.Length; line++)
            {
                var sourceCodeLine = _sourceCode[line];
                
                var position = 0;
                while (position < sourceCodeLine.Length)
                {
                    var (token, length) = NextToken(sourceCodeLine, position, line);

                    if (token != null)
                        tokens.Add(token);
                
                    position += length;
                }
            }

            return tokens;
        }

        private (Token?, int) NextToken(string sourceCodeLine, int position, int line)
        {
            var nextToken = sourceCodeLine.Substring(position);
            
            var tokenTypes = (TokenType[])Enum.GetValues(typeof(TokenType));
            foreach (var tokenType in tokenTypes)
            {
                var pattern = $"^{tokenType.GetRegex()}";
                var match = Regex.Match(nextToken, pattern);

                if (match.Success)
                {
                    Token? token = null;
                    
                    // Ignore whitespace, only used to divide two lexemes
                    if (tokenType != TokenType.Whitespace)
                    {
                        var tokenValue = match.Groups.Count > 1
                            ? match.Groups[1].Value // This gets the text literal without double quotes
                            : match.Value;

                        token = new Token(tokenType, tokenValue, line);
                    }
                    
                    return (token, match.Value.Length);
                }
            }
            
            throw new Exception($"Invalid expression: {nextToken}");
        }
    }
}