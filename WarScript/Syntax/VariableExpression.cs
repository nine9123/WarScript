#nullable enable

using System;
using WarScript.Syntax.Types;

namespace WarScript.Syntax
{
    public class VariableExpression : IExpression
    {
        public string Name { get; private set; }
        public Func<string, IValue?> VariableValue { get; private set; }
        public Action<string, IValue> VariableSetter { get; private set; }

        public VariableExpression(
            string name,
            Func<string, IValue?> variableValue,
            Action<string, IValue> variableSetter)
        {
            Name = name;
            VariableValue = variableValue;
            VariableSetter = variableSetter;
        }

        public IValue Evaluate()
        {
            var value = VariableValue(Name);
            
            if (value == null)
                return new TextValue(Name);

            return value;
        }

        public void SetValue(IValue value)
        {
            VariableSetter(Name, value);
        }
    }
}