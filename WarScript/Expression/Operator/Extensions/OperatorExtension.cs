using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WarScript.Expression.Operator;

namespace WarScript.Expression.Operator.Extensions
{
    public static class OperatorExtension
    {
        // Order matters — more specific patterns must come before broader ones,
        // mirroring the Java enum declaration order so regex precedence is preserved.
        // e.g. NestedClassInstance (:: new) before ClassProperty (::)
        //      FloorDivision (//) before Division (/)
        //      Exponentiation (**) before Multiplication (*)
        private static readonly List<(string Pattern, Operator Op)> OperatorPatterns = new List<(string Pattern, Operator Op)>()
        {
            ("!",           Operator.Not),
            ("new",         Operator.ClassInstance),
            (":{2}\\s+new", Operator.NestedClassInstance),
            (":{2}",        Operator.ClassProperty),
            ("as",          Operator.ClassCast),
            ("is",          Operator.ClassInstanceOf),

            ("\\*{2}",      Operator.Exponentiation),
            ("\\*",         Operator.Multiplication),
            ("//",          Operator.FloorDivision),
            ("/",           Operator.Division),
            ("%",           Operator.Modulo),

            ("\\+",         Operator.Addition),
            ("-",           Operator.Subtraction),

            ("==",          Operator.Equals),
            ("!=",          Operator.NotEquals),
            ("<=",          Operator.LessThanOrEqualTo),
            ("<",           Operator.LessThan),
            (">=",          Operator.GreaterThanOrEqualTo),
            (">",           Operator.GreaterThan),

            ("\\(",         Operator.LeftParen),
            ("\\)",         Operator.RightParen),

            ("and",         Operator.LogicalAnd),
            ("or",          Operator.LogicalOr),

            ("<<",          Operator.ArrayAppend),
            ("=",           Operator.Assignment),
        };

        // Mirrors Java's String.matches() which anchors the entire string
        public static Operator ToOperator(this string value)
        {
            foreach (var (pattern, op) in OperatorPatterns)
            {
                if (Regex.IsMatch(value, $"^(?:{pattern})$"))
                    return op;
            }
            throw new System.Exception($"Cannot parse '{value}' to an operator");
        }

        public static UnaryOperatorExpression ToUnaryExpression(this Operator op, IExpression operand) =>
            op switch
            {
                Operator.Not           => new NotOperator(operand),
                Operator.ClassInstance => new ClassInstanceOperator(operand),
                _ => throw new System.Exception($"Operator {op} is not a unary operator")
            };

        public static BinaryOperatorExpression ToBinaryExpression(this Operator op, IExpression left, IExpression right) =>
            op switch
            {
                Operator.Addition             => new AdditionOperator(left, right),
                Operator.Subtraction          => new SubtractionOperator(left, right),
                Operator.Multiplication       => new MultiplicationOperator(left, right),
                Operator.Division             => new DivisionOperator(left, right),
                Operator.FloorDivision        => new FloorDivisionOperator(left, right),
                Operator.Modulo               => new ModuloOperator(left, right),
                Operator.Exponentiation       => new ExponentiationOperator(left, right),
                Operator.Equals               => new EqualsOperator(left, right),
                Operator.NotEquals            => new NotEqualsOperator(left, right),
                Operator.LessThan             => new LessThanOperator(left, right),
                Operator.LessThanOrEqualTo    => new LessThanOrEqualToOperator(left, right),
                Operator.GreaterThan          => new GreaterThanOperator(left, right),
                Operator.GreaterThanOrEqualTo => new GreaterThanOrEqualToOperator(left, right),
                Operator.LogicalAnd           => new LogicalAndOperator(left, right),
                Operator.LogicalOr            => new LogicalOrOperator(left, right),
                Operator.Assignment           => new AssignmentOperator(left, right),
                Operator.ClassProperty        => new ClassPropertyOperator(left, right),
                Operator.ClassCast            => new ClassCastOperator(left, right),
                Operator.ClassInstanceOf      => new ClassInstanceOfOperator(left, right),
                Operator.NestedClassInstance  => new NestedClassInstanceOperator(left, right),
                Operator.ArrayAppend          => new ArrayAppendOperator(left, right),
                Operator.ArrayValue           => new ArrayValueOperator(left, right),
                _ => throw new System.Exception($"Operator {op} is not a binary operator")
            };

        public static bool IsBinary(this Operator op) => !IsUnary(op);

        public static bool IsUnary(this Operator op) =>
            op is Operator.Not || op is Operator.ClassInstance;

        public static int GetPrecedence(this Operator op) =>
            op switch
            {
                Operator.Not                  => 7,
                Operator.ClassInstance        => 7,
                Operator.NestedClassInstance  => 7,
                Operator.ClassProperty        => 7,
                Operator.ClassCast            => 7,
                Operator.ClassInstanceOf      => 7,
                Operator.ArrayValue => 7,

                Operator.Exponentiation       => 6,
                Operator.Multiplication       => 6,
                Operator.Division             => 6,
                Operator.FloorDivision        => 6,
                Operator.Modulo               => 6,

                Operator.Addition             => 5,
                Operator.Subtraction          => 5,

                Operator.Equals               => 4,
                Operator.NotEquals            => 4,
                Operator.LessThan             => 4,
                Operator.LessThanOrEqualTo    => 4,
                Operator.GreaterThan          => 4,
                Operator.GreaterThanOrEqualTo => 4,

                Operator.LeftParen            => 3,
                Operator.RightParen           => 3,

                Operator.LogicalAnd           => 2,
                Operator.LogicalOr            => 1,

                Operator.ArrayAppend          => 0,
                Operator.Assignment           => 0,

                _ => throw new System.Exception($"Operator {op} has no defined precedence")
            };

        // Mirrors Java: getPrecedence().compareTo(o.getPrecedence()) >= 0
        // >= (not >) is required for correct left-associative shunting-yard behaviour
        public static bool GreaterThan(this Operator op, Operator other) =>
            op.GetPrecedence().CompareTo(other.GetPrecedence()) >= 0;
    }
}