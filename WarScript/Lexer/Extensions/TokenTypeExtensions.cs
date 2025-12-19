using System;

namespace WarScript.Lexer.Extensions
{
    public static class TokenTypeExtensions
    {
        public static string GetRegex(this TokenType type)
        {
            switch (type)
            {
                case TokenType.LineBreak: return @"[\n\r]";
                case TokenType.Whitespace: return @"[\s\t]";
                case TokenType.Keyword: return @"(if|then|end|print|struct|arg)(?=\s|$)";
                case TokenType.GroupDivider: return @"(\[|\]|\,)";
                case TokenType.Logical: return @"(true|false)(?=\s|$)";
                case TokenType.Numeric: return "[0-9]+";
                case TokenType.Text: return "\"([^\"]*)\"";
                case TokenType.Operator: return @"(\+|\-|\>|\<|\={1,2}|\!|\:{2}|\*|\/|\(|\))|new(?=\s|$)";
                case TokenType.Variable: return "[a-zA-Z_]+[a-zA-Z0-9_]*";
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}