#nullable enable

using System;

namespace WarScript.Syntax.Operator
{
    public static class OperatorExtension
    {
        public static Operator? ToOperator(this string value)
        {
            switch (value)
            {
                case "!": return Operator.Not;
                case "+": return Operator.Addition;
                case "-": return Operator.Subtraction;
                case "==": return Operator.Equality;
                case ">": return Operator.GreaterThan;
                case "<": return Operator.LessThan;
                case "::": return Operator.StructureValue;
            }

            return null;
        }

        public static UnaryOperatorExpression ToOperatorExpression(this Operator op, IExpression left)
        {
            switch (op)
            {
                case Operator.Not: return new NotOperator(left);
            }

            throw new Exception($"Operator {op} is not supported");
        }
        
        public static BinaryOperatorExpression ToOperatorExpression(this Operator op, IExpression left, IExpression right)
        {
            switch (op)
            {
                case Operator.Addition: return new AdditionOperator(left, right);
                case Operator.Subtraction: return new SubtractionOperator(left, right);
                case Operator.Equality: return new EqualsOperator(left, right);
                case Operator.GreaterThan: return new GreaterThanOperator(left, right);
                case Operator.LessThan: return new LessThanOperator(left, right);
                case Operator.StructureValue: return new StructureValueOperator(left, right);
            }

            throw new Exception($"Operator {op} is not supported");
        }

        public static bool SupportsTwoOperands(this Operator op)
        {
            if (op == Operator.Not)
                return false;

            return true;
        }
    }
}