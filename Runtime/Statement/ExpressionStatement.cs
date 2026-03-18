using WarScript.Context;
using WarScript.Expression;

namespace WarScript.Statement
{
    public class ExpressionStatement : Statement
    {
        public readonly IExpression Expression;

        public ExpressionStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            Expression = expression;
        }
        
        public override void Execute()
        {
            Expression.Evaluate();
            _script.ExceptionContext.AddTracedStatement(this);
        }
    }
}