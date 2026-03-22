using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Statement.Loop
{
    public class ForLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variable;
        private readonly IExpression _lowerBound;
        private readonly IExpression _upperBound;
        private readonly IExpression _step;

        // ── Numeric fast-path state ──
        // Evaluated once in Init(), used directly per iteration.
        // Bypasses the entire expression evaluation pipeline (no virtual dispatch,
        // no WarValue struct copies for HasNext/PostIncrement).
        private double _counter;
        private double _upperBoundValue;
        private double _stepValue;

        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName,
            VariableExpression variable, IExpression lowerBound, IExpression upperBound)
            : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
            _step = script.DefaultStep;
        }

        public ForLoopStatement(WarScriptLanguage script, int rowNumber, string blockName,
            VariableExpression variable, IExpression lowerBound, IExpression upperBound, IExpression step)
            : base(script, rowNumber, blockName)
        {
            _variable = variable;
            _lowerBound = lowerBound;
            _upperBound = upperBound;
            _step = step;
        }

        protected override void Init()
        {
            var lower = _lowerBound.Evaluate();
            var upper = _upperBound.Evaluate();
            var step = _step.Evaluate();

            _counter = lower.Numeric;
            _upperBoundValue = upper.Numeric;
            _stepValue = step.Numeric;

            _script.MemoryContext.GetScope().SetLocal(_variable.Name, lower);
        }

        protected override bool HasNext()
        {
            // Direct double comparison — no expression evaluation, no struct copies
            return _counter < _upperBoundValue;
        }

        protected override void PreIncrement() { }

        protected override void PostIncrement()
        {
            // Direct double arithmetic — one add, one SetLocal
            _counter += _stepValue;
            _script.MemoryContext.GetScope().SetLocal(_variable.Name, WarValue.FromNumeric(_counter));
        }
    }
}
