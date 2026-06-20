using WarScript.Context;

namespace WarScript.Statement.Loop
{
    public class NextStatement : Statement
    {
        public NextStatement(WarScriptLanguage script, int rowNumber, string blockName) : base(script, rowNumber, blockName)
        {
        }
        
        public override void Execute()
        {
            _script.NextContext.GetScope().Invoke();
            _script.HaltFlags |= WarScriptLanguage.HaltFlag.Next;
        }
    }
}