#nullable enable

using System.Collections.Generic;

namespace WarScript.Expression
{
    public class ClassExpression : IExpression
    {
        private readonly string _name;
        private readonly List<IExpression> _propertiesExpressions;

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
