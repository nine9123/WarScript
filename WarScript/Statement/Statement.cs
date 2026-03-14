namespace WarScript.Statement
{
    public abstract class Statement
    {
        public readonly int? RowNumber;
        public readonly string BlockName;

        public Statement(int? rowNumber, string blockName)
        {
            RowNumber = rowNumber;
            BlockName = blockName;
        }
        
        public abstract void Execute();
    }
}