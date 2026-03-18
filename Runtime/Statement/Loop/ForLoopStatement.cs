using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class ForLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variable;
        private readonly IExpression _lowerBound;
        private readonly IExpression _upperBound;
        private readonly IExpression _step;

        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variable, IExpression lowerBound, IExpression upperBound) : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
            _step = _script.DefaultStep;
        }
        
        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName, VariableExpression variable, IExpression lowerBound, IExpression upperBound, IExpression step) : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
            _step = step;
        }

        protected override void Init()
        {
            _script.MemoryContext.GetScope().Set(_variable.Name, _lowerBound.Evaluate());
        }

        protected override bool HasNext()
        {
            var hasNext = new LessThanOperator(_script, _variable, _upperBound);
            var value = hasNext.Evaluate();
            return value is LogicalValue logicalValue && logicalValue.GetValue();
        }

        protected override void PreIncrement()
        {
        }

        protected override void PostIncrement()
        {
            var stepOperator = new AdditionOperator(_script, _variable, _step);
            _script.MemoryContext.GetScope().Set(_variable.Name, stepOperator.Evaluate());
        }
    }
}