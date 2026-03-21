using WarScript.Context;
using WarScript.Expression;
using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ClassPropertyOperator : BinaryOperatorExpression, IAssignExpression
    {
        public ClassPropertyOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override IValue Evaluate()
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // access class's property via this instance
            // this :: class_argument
            if (left is ThisValue thisValue)
                left = thisValue.GetValue();

            if (left is ClassValue classInstance)
            {
                if (Right is VariableExpression varExpr)
                    // access class's property
                    // new Class [] :: class_property
                    return classInstance.GetValue(varExpr.Name);

                if (Right is FunctionExpression funcExpr)
                    // execute class's function
                    // new Class [] :: class_function []
                    return funcExpr.Evaluate(classInstance);

                // access class's array/string property by index
                // this :: array_property{index}
                if (Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
                {
                    var propValue = classInstance.GetValue(arrayVar.Name);
                    if (propValue == null) return null;
                    var index = arrayOp.Right.Evaluate();
                    if (index == null) return null;

                    if (propValue is ArrayValue arrVal && index is NumericValue numIdx)
                        return arrVal.GetValue((int)numIdx.GetValue());
                    if (propValue is TextValue textVal && index is NumericValue numIdx2)
                        return textVal.GetValue((int)numIdx2.GetValue());
                }
            }

            return _script.ExceptionContext.RaiseException($"Unable to access class's property `{Right}`");
        }

        public IValue Assign(IValue value)
        {
            var left = Left.Evaluate();
            if (left == null) return null;

            // access class's property via this instance
            // this :: class_argument
            if (left is ThisValue thisValue)
                left = thisValue.GetValue();

            if (left is ClassValue classInstance)
            {
                if (Right is VariableExpression varExpr)
                {
                    classInstance.SetValue(varExpr.Name, value);
                }
                // assign to class's array/string property by index
                // this :: array_property{index} = value
                else if (Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
                {
                    var propValue = classInstance.GetValue(arrayVar.Name);
                    var index = arrayOp.Right.Evaluate();
                    if (propValue != null && index != null)
                    {
                        if (propValue is ArrayValue arrVal && index is NumericValue numIdx)
                            arrVal.SetValue((int)numIdx.GetValue(), value);
                        else if (propValue is TextValue textVal && index is NumericValue numIdx2)
                            textVal.SetValue((int)numIdx2.GetValue(), value);
                    }
                }
            }

            return left;
        }
    }
}