using System.Collections.Generic;
using WarScript.Expression.Value;

namespace WarScript.Expression
{
    public sealed class ArrayExpression : IExpression
    {
        public List<IExpression> Values { get; private set; }
        private readonly WarScriptLanguage _script;

        public ArrayExpression(WarScriptLanguage script, List<IExpression> values)
        {
            _script = script;
            Values = values;
        }

        public WarValue Evaluate()
        {
            return WarValue.FromArray(GetValues());
        }

        public List<WarValue> GetValues()
        {
            var values = new List<WarValue>(Values.Count);
            foreach (var expr in Values)
                values.Add(expr.Evaluate());
            return values;
        }
    }
}
