namespace WarScript.Lexer
{
    public class Token
    {
        public TokenType Type { get; private set; }
        public string Value { get; private set; }
        public readonly int Line;

        public Token(TokenType type, string value, int line)
        {
            Type = type;
            Value = value;
            Line = line;
        }

        public override string ToString()
        {
            return $"[{Line}] {Type}:\t{Value}";
        }
    }
}