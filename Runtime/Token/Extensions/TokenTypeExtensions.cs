using System;

namespace WarScript.Token.Extensions
{
    public static class TokenTypeExtensions
    {
        public static string GetRegex(this TokenType type)
        {
            switch (type)
            {
                case TokenType.Comment:
                    return @"\#.*";

                case TokenType.LineBreak:
                    return @"[\n\r]";

                case TokenType.Whitespace:
                    return @"[\s\t]";

                case TokenType.Keyword:
                    return @"(if|elif|else|end|print|class|fun|return|loop|in|by|break|next|assert|raise|begin|rescue|ensure|import)(?=\s|$)(?!_)";

                case TokenType.GroupDivider:
                    // [ ] , { } .. :  (single colon, not double)
                    return @"(\[|\]|\,|\{|\}|\.{2}|(\:(?!\:)))";

                case TokenType.Logical:
                    return @"(true|false)(?=[,\s\]\)]|$)(?!_)";

                case TokenType.Numeric:
                    // supports negatives and decimals, excludes .. (range operator)
                    return @"([-]?(?=[.]?[0-9])[0-9]*(?![.]{2})[.]?[0-9]*)";

                case TokenType.Null:
                    return @"(null)(?=,|\s|$)(?!_)";

                case TokenType.This:
                    return @"(this)(?=,|\s|$)(?!_)";

                case TokenType.Text:
                    return "\"([^\"]*)\"";

                case TokenType.Operator:
                    // Order matters: longer/more specific patterns before shorter ones
                    // e.g. ** before *, // before /, :: new before ::, >=  before >, <= and << before <, == and != before = and !
                    return @"(\+|-|\*{1,2}|/{1,2}|%|>=?|<=|<{1,2}|={1,2}|!=|!|:{2}\s+new|:{2}|\(|\)|(new|and|or|as|is)(?=\s|$)(?!_))";

                case TokenType.Variable:
                    return "[a-zA-Z_]+[a-zA-Z0-9_]*";

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}