#nullable enable

using System.Collections.Generic;

namespace WarScript.Expression
{
    public class FunctionExpression : IExpression
    {
        public string Name { get; private set; }
        public List<IExpression> ArgumentExpression { get; private set; }

        public FunctionExpression(WarScriptLanguage script, string name, List<IExpression> argumentExpression)
        {
            Name = name;
            ArgumentExpression = argumentExpression;
        }
    }
}
