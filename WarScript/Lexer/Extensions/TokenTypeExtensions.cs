using System;

namespace WarScript.Lexer.Extensions
{
    public static class TokenTypeExtensions
    {
        public static string GetRegex(this TokenType type)
        {
            switch (type)
            {
                case TokenType.Whitespace: return @"[\s\t\n\r]";
                case TokenType.Keyword: return @"\b(if|then|end|print|struct|arg|new)\b";
                case TokenType.GroupDivider: return @"(\[|\])";
                case TokenType.Logical: return @"\b(true|false)\b";
                case TokenType.Numeric: return @"[0-9]+";
                case TokenType.Text: return "\"([^\"]*)\"";
                case TokenType.Variable: return @"[a-zA-Z_]+[a-zA-Z0-9_]*";
                case TokenType.Operator: return @"(\+|\-|\>|\<|\={1,2}|\!|\:{2})";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}