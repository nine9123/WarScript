using System.Collections.Generic;

namespace WarScript.Expression
{
    public sealed class ArrayExpression : IExpression
    {
        public List<IExpression> Values { get; private set; }

        public ArrayExpression(WarScriptLanguage script, List<IExpression> values)
        {
            Values = values;
        }
    }
}
