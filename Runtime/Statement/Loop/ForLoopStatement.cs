using WarScript.Expression;

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
    }
}
