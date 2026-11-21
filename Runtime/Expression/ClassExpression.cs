#nullable enable

using System.Collections.Generic;

namespace WarScript.Expression
{
    public class ClassExpression : IPositionedExpression
    {
        private readonly string _name;
        private readonly List<IExpression> _propertiesExpressions;

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Line { get; internal set; }

        /// <inheritdoc cref="IPositionedExpression"/>
        public int Column { get; internal set; }

        // Compiler accessors
        internal string ClassName => _name;
        internal List<IExpression> PropertiesExpressions => _propertiesExpressions;

        public ClassExpression(WarScriptLanguage script, string name, List<IExpression> propertiesExpressions)
        {
            _name = name;
            _propertiesExpressions = propertiesExpressions;
        }
    }
}
