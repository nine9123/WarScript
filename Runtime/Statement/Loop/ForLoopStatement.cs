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

        /// <summary>The column of the range's <c>..</c> (0 = unknown), where the bound check <c>i &lt; limit</c> is reported (T3.8).</summary>
        internal int RangeColumn { get; set; }

        /// <summary>The column of the step after <c>by</c> (0 = unknown or no <c>by</c>), where a step that is not positive and the addition <c>i + step</c> are reported (T3.8).</summary>
        internal int StepColumn { get; set; }

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
