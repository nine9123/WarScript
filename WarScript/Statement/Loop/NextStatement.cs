using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public class NextStatement : Statement
    {
        public NextStatement(int rowNumber, string blockName) : base(rowNumber, blockName)
        {
        }
        
        public override void Execute()
        {
            NextContext.GetScope().Invoke();
        }
    }
}