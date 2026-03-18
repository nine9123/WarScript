namespace WarScript.Statement
{
    public abstract class Statement
    {
        protected readonly WarScriptLanguage _script;
        public readonly int? RowNumber;
        public readonly string BlockName;

        protected Statement(WarScriptLanguage script, int? rowNumber, string blockName)
        {
            _script = script;
            RowNumber = rowNumber;
            BlockName = blockName;
        }
        
        public abstract void Execute();
    }
}