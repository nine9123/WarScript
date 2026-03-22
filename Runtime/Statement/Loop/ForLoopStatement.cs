using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class ForLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variable;
        private readonly IExpression _lowerBound;
        private readonly LessThanOperator _hasNextOperator;
        private readonly AdditionOperator _stepOperator;

        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variable, IExpression lowerBound, IExpression upperBound) : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _hasNextOperator = new LessThanOperator(_script, _variable, upperBound);
            _stepOperator = new AdditionOperator(_script, _variable, _script.DefaultStep);
        }

        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variable, IExpression lowerBound, IExpression upperBound, IExpression step) : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _hasNextOperator = new LessThanOperator(_script, _variable, upperBound);
            _stepOperator = new AdditionOperator(_script, _variable, step);
        }

        protected override void Init()
        {
            _script.MemoryContext.GetScope().SetLocal(_variable.Name, _lowerBound.Evaluate());
        }

        protected override bool HasNext()
        {
            var value = _hasNextOperator.Evaluate();
            return value.IsLogical && value.LogicalValue;
        }

        protected override void PreIncrement() { }

        protected override void PostIncrement()
        {
            _script.MemoryContext.GetScope().SetLocal(_variable.Name, _stepOperator.Evaluate());
        }
    }
}
