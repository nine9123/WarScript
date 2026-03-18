using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement
{
    public class AssertStatement : Statement
    {
        public readonly IExpression Expression;
        
        public AssertStatement(WarScriptLanguage script, int rowNumber, string blockName, IExpression expression) : base(script, rowNumber, blockName)
        {
            Expression = expression;
        }

        public override void Execute()
        {
            var value = Expression.Evaluate();
            if (value is LogicalValue logicalValue && !logicalValue.GetValue())
            {
                _script.ExceptionContext.RaiseException("Assertion error");
                _script.ExceptionContext.AddTracedStatement(this);
            }
        }
    }
}