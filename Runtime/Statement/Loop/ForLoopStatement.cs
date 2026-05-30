using WarScript.Expression;
using WarScript.Expression.Value;
using FixMath;

namespace WarScript.Statement.Loop
{
    public class ForLoopStatement : AbstractLoopStatement
    {
        private readonly VariableExpression _variable;
        private readonly IExpression _lowerBound;
        private readonly IExpression _upperBound;
        private readonly IExpression _step;

        // Compiler accessors
        internal VariableExpression Variable => _variable;
        internal IExpression LowerBound => _lowerBound;
        internal IExpression UpperBound => _upperBound;
        internal IExpression Step => _step;

        // Numeric fast-path state — mutable per-execution.
        // Saved/restored in Execute() to handle recursive reentry
        // (AST caching means the same node is shared across recursive calls).
        private F64 _counter;
        private F64 _upperBoundValue;
        private F64 _stepValue;

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

        /// <summary>
        /// Save and restore fast-path state around base.Execute() so recursive
        /// reentry through the same cached AST node doesn't clobber the outer loop.
        /// </summary>
        public override void Execute()
        {
            var savedCounter = _counter;
            var savedUpper = _upperBoundValue;
            var savedStep = _stepValue;
            try
            {
                base.Execute();
            }
            finally
            {
                _counter = savedCounter;
                _upperBoundValue = savedUpper;
                _stepValue = savedStep;
            }
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
            return _counter < _upperBoundValue;
        }

        protected override void PreIncrement() { }

        protected override void PostIncrement()
        {
            _counter += _stepValue;
            _script.MemoryContext.GetScope().SetLocal(_variable.Name, WarValue.FromNumeric(_counter));
        }
    }
}
