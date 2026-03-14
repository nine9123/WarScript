using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public class BreakStatement : Statement
    {
        public BreakStatement(int rowNumber, string blockName) : base(rowNumber, blockName)
        {
        }
        
        public override void Execute()
        {
            BreakContext.GetScope().Invoke();
        }
    }
}