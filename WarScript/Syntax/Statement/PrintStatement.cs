using System;

namespace WarScript.Syntax.Statement
{
    public class PrintStatement : IStatement
    {
        public readonly IExpression Expression;

        public PrintStatement(IExpression expression)
        {
            Expression = expression;
        }

        public void Execute()
        {
            var value = Expression.Evaluate();
            Console.WriteLine(value.ToString());
        }
    }
}