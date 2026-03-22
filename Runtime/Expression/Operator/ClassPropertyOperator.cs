using WarScript.Expression.Value;

namespace WarScript.Expression.Operator
{
    public sealed class ClassPropertyOperator : BinaryOperatorExpression, IAssignExpression
    {
        public ClassPropertyOperator(WarScriptLanguage script, IExpression left, IExpression right) : base(script, left, right) { }

        public override WarValue Evaluate()
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsClass)
            {
                var classData = left.ClassValue;

                if (Right is VariableExpression varExpr)
                    return classData.GetProperty(varExpr.Name);

                if (Right is FunctionExpression funcExpr)
                    return funcExpr.Evaluate(classData);

                if (Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
                {
                    var propValue = classData.GetProperty(arrayVar.Name);
                    var index = arrayOp.Right.Evaluate();
                    if (_script.HaltFlags != 0) return default;

                    if (propValue.IsArray && index.IsNumeric)
                        return propValue.GetArrayElement((int)index.Numeric);
                    if (propValue.IsText && index.IsNumeric)
                        return propValue.GetTextChar((int)index.Numeric);
                }
            }

            return _script.RaiseException($"Unable to access class's property `{Right}`");
        }

        public WarValue Assign(WarValue value)
        {
            var left = Left.Evaluate();
            if (_script.HaltFlags != 0) return default;

            if (left.IsClass)
            {
                var classData = left.ClassValue;

                if (Right is VariableExpression varExpr)
                {
                    classData.SetProperty(varExpr.Name, value);
                }
                else if (Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
                {
                    var propValue = classData.GetProperty(arrayVar.Name);
                    var index = arrayOp.Right.Evaluate();
                    if (_script.HaltFlags != 0) return default;

                    if (propValue.IsArray && index.IsNumeric)
                        propValue.SetArrayElement((int)index.Numeric, value);
                    else if (propValue.IsText && index.IsNumeric)
                    {
                        var newText = propValue.SetTextChar((int)index.Numeric, value.ToString());
                        classData.SetProperty(arrayVar.Name, newText);
                    }
                }
            }

            return left;
        }
    }
}
