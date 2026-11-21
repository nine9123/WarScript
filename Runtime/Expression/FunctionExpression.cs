#nullable enable

using System.Collections.Generic;

namespace WarScript.Expression
{
    public class FunctionExpression : IPositionedExpression
    {
        public string Name { get; private set; }
        public List<IExpression> ArgumentExpression { get; private set; }

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Line { get; internal set; }

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Column { get; internal set; }

        public FunctionExpression(WarScriptLanguage script, string name, List<IExpression> argumentExpression)
        {
            Name = name;
            ArgumentExpression = argumentExpression;
        }
    }
}
