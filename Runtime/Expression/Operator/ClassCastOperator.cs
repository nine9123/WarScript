using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ClassCastOperator : BinaryOperatorExpression
    {
        public ClassCastOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            var classData = left.ClassValue;
            var typeToCastName = ((VariableExpression)Right).Name;

            if (classData.Definition.ClassDetails.Name == typeToCastName)
                return left;

            var relation = classData.GetRelation(typeToCastName);
            return relation != null ? WarValue.FromClass(relation) : WarValue.Null;
        }
    }
}
