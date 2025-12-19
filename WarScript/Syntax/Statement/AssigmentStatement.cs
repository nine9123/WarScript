using System;

namespace WarScript.Syntax.Statement
{
    public class AssigmentStatement : IStatement
    {
        public readonly string Name;
        public readonly IExpression Expression;
        public readonly Action<string, IValue> VariableSetter;

        public AssigmentStatement(string name, IExpression expression, Action<string, IValue> variableSetter)
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