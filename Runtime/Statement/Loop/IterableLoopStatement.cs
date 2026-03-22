using System.Collections.Generic;
using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class IterableLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variableExpression;
        private readonly IExpression _iterableExpression;

        private IEnumerator<IValue> _iterator;
        private bool _hasNext;

        public IterableLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variableExpression, IExpression iterableExpression) : base(script, rowNumber, blockName)
        {
            _variableExpression = variableExpression;
            _iterableExpression = iterableExpression;
        }

        protected override void Init()
        {
            var value = _iterableExpression.Evaluate();
            if (value is IEnumerable<IValue> iterable)
            {
                _iterator = iterable.GetEnumerator();
                // Prime the enumerator
                _hasNext = _iterator.MoveNext();
                // Pre-create the loop variable in the counter scope so it
                // shadows any outer variable with the same name.
                // This mirrors ForLoopStatement.Init() which also uses SetLocal.
                _script.MemoryContext.GetScope().SetLocal(
                    _variableExpression.Name,
                    _hasNext ? _iterator.Current : _script.Null);
            }
            else
            {
                _script.ExceptionContext.RaiseException($"Unable to iterate `{value}`");
            }
        }

        protected override bool HasNext()
        {
            return _hasNext;
        }

        protected override void PreIncrement()
        {
            // Set the current value into scope
            _script.MemoryContext.GetScope().SetLocal(_variableExpression.Name, _iterator.Current);
            // Advance and cache whether a next element exists
            _hasNext = _iterator.MoveNext();
        }

        protected override void PostIncrement()
        {
        }
    }
}