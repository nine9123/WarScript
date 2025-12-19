#nullable enable

using System;

namespace WarScript.Syntax.Operator
{
    public static class OperatorExtension
    {
        public static Operator ToOperator(this string value)
        {
            switch (value)
            {
                case "=": return Operator.Assigment;
                
                case "!": return Operator.Not;
                case "::": return Operator.StructureValue;
                case "new": return Operator.StructureInstance;
                
                case "*": return Operator.Multiplication;
                case "/": return Operator.Division;
                
                case "+": return Operator.Addition;
                case "-": return Operator.Subtraction;
                
                case "<": return Operator.LessThan;
                case ">": return Operator.GreaterThan;
                
                case "(": return Operator.LeftParen;
                case ")": return Operator.RightParen;
                
                case "==": return Operator.Equality;
            }

            throw new Exception($"Can not parse {value} to an operator");
        }

        public static UnaryOperatorExpression ToOperatorExpression(this Operator op, IExpression left)
        {
            switch (op)
            {
                case Operator.Not: return new NotOperator(left);
                case Operator.StructureInstance: return new StructureInstanceOperator(left);
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
                case Operator.Multiplication: return new MultiplicationOperator(left, right);
                case Operator.Division: return new DivisionOperator(left, right);
                case Operator.Assigment: return new AssigmentOperator(left, right);
            }

            throw new Exception($"Operator {op} is not supported");
        }

        public static bool SupportsTwoOperands(this Operator op)
        {
            if (op == Operator.Not ||
                op == Operator.StructureInstance)
            {
                return false;
            }

            return true;
        }

        public static int GetPrecedence(this Operator op)
        {
            switch (op)
            {
                case Operator.Not: return 5;
                case Operator.StructureValue: return 5;
                case Operator.StructureInstance: return 5;
                
                case Operator.Multiplication: return 4;
                case Operator.Division: return 4;
                
                case Operator.Addition: return 3;
                case Operator.Subtraction: return 3;
                
                case Operator.Equality: return 2;
                case Operator.LessThan: return 2;
                case Operator.GreaterThan: return 2;
                
                case Operator.LeftParen: return 1;
                case Operator.RightParen: return 1;
             
                case Operator.Assigment: return 0;
            }

            throw new Exception($"Operator {op} is not supported");
        }

        public static bool GreaterThan(this Operator op, Operator other)
        {
            return op.GetPrecedence().CompareTo(other.GetPrecedence()) > 0;
        }
    }
}