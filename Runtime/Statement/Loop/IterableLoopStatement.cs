using System.Collections.Generic;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class IterableLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variableExpression;
        private readonly IExpression _iterableExpression;

        private List<WarValue> _items;
        private int _index;

        public IterableLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variableExpression, IExpression iterableExpression) : base(script, rowNumber, blockName)
        {
            _variableExpression = variableExpression;
            _iterableExpression = iterableExpression;
        }

        protected override void Init()
        {
            var value = _iterableExpression.Evaluate();
            if (value.IsArray)
            {
                _items = value.ArrayValue;
                _index = 0;
                // Pre-create the loop variable in the counter scope
                _script.MemoryContext.GetScope().SetLocal(
                    _variableExpression.Name,
                    _items.Count > 0 ? _items[0] : WarValue.Null);
            }
            else if (value.IsClass)
            {
                // Iterate class properties
                var classData = value.ClassValue;
                var properties = classData.Definition.ClassDetails.Properties;
                _items = new List<WarValue>(properties.Count);
                for (int i = 0; i < properties.Count; i++)
                    _items.Add(classData.GetProperty(properties[i]));
                _index = 0;
                _script.MemoryContext.GetScope().SetLocal(
                    _variableExpression.Name,
                    _items.Count > 0 ? _items[0] : WarValue.Null);
            }
            else
            {
                _items = null;
                _script.ExceptionContext.RaiseException($"Unable to iterate `{value}`");
            }
        }

        protected override bool HasNext()
        {
            return _items != null && _index < _items.Count;
        }

        protected override void PreIncrement()
        {
            _script.MemoryContext.GetScope().SetLocal(_variableExpression.Name, _items[_index]);
            _index++;
        }

        protected override void PostIncrement() { }
    }
}
