namespace WarScript.Token
{
    /// <summary>
    /// Token (lexeme) details
    /// </summary>
    public class Token
    {
        /// <summary>
        /// Type of the token
        /// </summary>
        public readonly TokenType Type;
        
        /// <summary>
        /// Value of the token
        /// </summary>
        public readonly string Value;
        
        /// <summary>
        /// Row number where the token is defined
        /// </summary>
        public readonly int RowNumber;

        public Token(TokenType type, string value, int rowNumber)
        {
            Type = type;
            Value = value;
            RowNumber = rowNumber;
        }

        public override string ToString()
        {
            return $"[{RowNumber}] {Type}:\t{Value}";
        }
    }
}