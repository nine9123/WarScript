namespace WarScript.Syntax.Operator
{
    public class StructureInstanceOperator : UnaryOperatorExpression
    {
        public StructureInstanceOperator(IExpression expression) : base(expression)
        {
        }
        
        public override IValue Calc(IValue value)
        {
            return value;
        }
    }
}