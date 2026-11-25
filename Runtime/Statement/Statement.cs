namespace WarScript.Statement
{
    public abstract class Statement
    {
        protected readonly WarScriptLanguage _script;
        public readonly int? RowNumber;
        public readonly string BlockName;

        /// <summary>
        /// Column of the statement's first token, from 1; 0 when unknown (a
        /// statement desugared on another line, or one the parser built
        /// without a token). Set by the parser after construction (T3.1).
        /// </summary>
        public int Column { get; internal set; }

        protected Statement(WarScriptLanguage script, int? rowNumber, string blockName)
        {
            _script = script;
            RowNumber = rowNumber;
            BlockName = blockName;
        }
    }
}