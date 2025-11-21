#nullable enable

using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax
{
    public class VariableExpression : IExpression
    {
        public string Name { get; private set; }
        public Func<string, IValue?> VariableValue { get; private set; }

        public VariableExpression(string name, Func<string, IValue?> variableValue)
        {
            Name = name;
            VariableValue = variableValue;
        }

        public IValue Evaluate()
        {
            var value = VariableValue(Name);
            
            if (value == null)
                return new TextValue(Name);

            return value;
        }
    }
}