#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Syntax.Types;

namespace WarScript.Syntax
{
    public class StructureExpression : IExpression, IComparable<StructureExpression>
    {
        public StructureDefinition Definition { get; private set; }
        public List<IExpression> Values { get; private set; }
        public Func<string, IValue> VariableValue { get; private set; }

        public StructureExpression(StructureDefinition definition, List<IExpression> values, Func<string, IValue> variableValue)
        {
            Definition = definition;
            Values = values;
            VariableValue = variableValue;
        }

        public override string ToString()
        {
            var text = $"struct {Definition.Name}";
            text += "\n";
            
            for (var i = 0; i < Definition.Arguments.Count; i++)
            {
                text += $"\targ {Definition.Arguments[i]}: {GetValue(i)}\n";
            }
            
            return text;
        }

        public IValue Evaluate()
        {
            return new StructureValue(this);
        }

        // TODO: This can return null, is this correct? Handle it.
        public IValue? GetArgumentValue(string field)
        {
            for (var i = 0; i < Definition.Arguments.Count; i++)
            {
                var argument = Definition.Arguments[i];
                if (argument == field)
                {
                    return GetValue(i);
                }
            }

            return null;
        }

        // TODO: VariableValue could return null
        private IValue GetValue(int index)
        {
            var expression = Values[index];
            if (expression is VariableExpression variableExpression)
                return VariableValue(variableExpression.Name);
            else
                return expression.Evaluate();
        }

        // TODO: Is this needed?
        /*
        public int compareTo(StructureExpression o) {
            for (String field : definition.getArguments()) {
                Value<?> value = getArgumentValue(field);
                Value<?> oValue = o.getArgumentValue(field);
                if (value == null && oValue == null) continue;
                if (value == null) return -1;
                if (oValue == null) return 1;
                //noinspection unchecked,rawtypes
                int result = ((Comparable) value.getValue()).compareTo(oValue.getValue());
                if (result != 0) return result;
            }
            return 0;
        }
        */
        public int CompareTo(StructureExpression other)
        {
            throw new NotImplementedException();
        }
    }
}