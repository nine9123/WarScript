using System.Collections.Generic;
using System.Text.RegularExpressions;
using WarScript.Exception;
using WarScript.Token;
using WarScript.Token.Extensions;

namespace WarScript
{
    /// <summary>
    /// Transforming the source code into tokens.
    /// <see cref="Token"/>
    /// <see cref="TokenType"/>
    /// </summary>
    public class LexicalParser
    {
        private readonly List<Token.Token> _tokens;
        private readonly string _source;
        private int _rowNumber;

        /// <summary>
        /// Parse incoming source code into a list of tokens following the TokenType regex rules.
        /// </summary>
        public static List<Token.Token> Parse(string sourceCode)
        {
            var parser = new LexicalParser(sourceCode);
            parser.Parse();
            return parser._tokens;
        }

        private LexicalParser(string source)
        {
            _source = source;
            _tokens = new List<Token.Token>();
            _rowNumber = 1;
        }

        private void Parse()
        {
            int position = 0;
            while (position < _source.Length)
                position += NextToken(position);
        }

        // Find the next token starting at position.
        // Returns the number of characters consumed (i.e. the length of the matched text).
        private int NextToken(int position)
        {
            var remaining = _source.Substring(position);

            foreach (TokenType tokenType in System.Enum.GetValues(typeof(TokenType)))
            {
                var pattern = new Regex("^(?:" + tokenType.GetRegex() + ")");
                var match = pattern.Match(remaining);

                if (match.Success)
                {
                    if (tokenType != TokenType.Whitespace)
                    {
                        // group 1 is used to get text literal without double quotes
                        var value = match.Groups.Count > 1 && match.Groups[1].Success
                            ? match.Groups[1].Value
                            : match.Value;

                        _tokens.Add(new Token.Token(tokenType, value, _rowNumber));

                        if (tokenType == TokenType.LineBreak)
                            _rowNumber++;
                    }

                    return match.Length;
                }
            }

            throw new SyntaxException($"Invalid expression at line {_rowNumber}");
        }
    }
}