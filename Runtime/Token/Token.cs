namespace WarScript.Token
{
    /// <summary>
    /// Token (lexeme) details
    /// </summary>
    public readonly struct Token
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

        /// <summary>
        /// Column of the token's first character, from 1 (T3.1). A column is
        /// one UTF-16 code unit of the line, so a tab counts as one column.
        /// Tokens desugared from an interpolation carry the column of the
        /// interpolation's <c>{</c>; 0 means unknown.
        /// </summary>
        public readonly int Column;

        public Token(TokenType type, string value, int rowNumber, int column = 0)
        {
            Type = type;
            Value = value;
            RowNumber = rowNumber;
            Column = column;
        }

        public override string ToString()
        {
            return System.FormattableString.Invariant($"[{RowNumber}] {Type}:\t{Value}");
        }
    }
}
