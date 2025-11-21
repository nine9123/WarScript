using System;

namespace WarScript.Syntax.Statement
{
    public class AssignStatement : IStatement
    {
        public readonly string Name;
        public readonly IExpression Expression;
        public readonly Action<string, IValue> VariableSetter;

        public AssignStatement(string name, IExpression expression, Action<string, IValue> variableSetter)
        {
            Name = name;
            Expression = expression;
            VariableSetter = variableSetter;
        }

        public void Execute()
        {
            VariableSetter(Name, Expression.Evaluate());
        }
    }
}