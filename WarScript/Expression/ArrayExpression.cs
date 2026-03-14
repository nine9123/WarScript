using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public class ArrayExpression : IExpression
    {
        public List<IExpression> Values { get; private set; }

        public ArrayExpression(List<IExpression> values)
        {
            Values = values;
        }
        
        public IValue Evaluate()
        {
            return new ArrayValue(this);
        }
        
        public List<IValue> GetValues()
        {
            var values = new List<IValue>();
            foreach (var expressionValue in Values)
            {
                values.Add(expressionValue.Evaluate());
            }

            return values;
        }
    }
}