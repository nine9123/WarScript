namespace WarScript.Syntax.Operator
{
    public class AssigmentOperator : BinaryOperatorExpression
    {
        public AssigmentOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (Left is VariableExpression variableExpression)
                variableExpression.SetValue(right);

            return left;
        }
    }
}