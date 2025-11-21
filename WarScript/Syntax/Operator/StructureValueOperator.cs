using WarScript.Syntax.Types;

namespace WarScript.Syntax.Operator
{
    public class StructureValueOperator : BinaryOperatorExpression
    {
        public StructureValueOperator(IExpression left, IExpression right) : base(left, right)
        {
        }

        public override IValue Calc(IValue left, IValue right)
        {
            if (left is StructureValue structureValue)
                return structureValue.ValueField.GetArgumentValue(right.ToString());

            return left;
        }
    }
}