using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public class BreakStatement : Statement
    {
        public BreakStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }
        
        public override void Execute()
        {
            _script.BreakContext.GetScope().Invoke();
            _script.HaltFlags |= WarScriptLanguage.HaltFlag.Break;
        }
    }
}