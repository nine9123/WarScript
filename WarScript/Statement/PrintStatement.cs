using System;
using WarScript.Context;
using WarScript.Expression;

namespace WarScript.Statement
{
    public class PrintStatement : Statement
    {
        public readonly IExpression Expression;

        public PrintStatement(int rowNumber, string blockName, IExpression expression) : base(rowNumber, blockName)
        {
            Expression = expression;
        }

        public override void Execute()
        {
            var value = Expression.Evaluate();
            if (value != null)
            {
                Console.WriteLine(value.ToString());
            }
            ExceptionContext.AddTracedStatement(this);
        }
    }
}