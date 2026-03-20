using System.Collections.Generic;

namespace WarScript.Expression.Operator.Extensions
{
    public static class OperatorExtension
    {
        // Order matters: more specific patterns must come before broader ones
        private static readonly Dictionary<string, Operator> OperatorMap = new Dictionary<string, Operator>()
        {
            { "!",    Operator.Not },
            { "new",  Operator.ClassInstance },
            { "::",   Operator.ClassProperty },
            { "as",   Operator.ClassCast },
            { "is",   Operator.ClassInstanceOf },

            { "*",    Operator.Multiplication },
            { "/",    Operator.Division },
            { "%",    Operator.Modulo },

            { "+",    Operator.Addition },
            { "-",    Operator.Subtraction },

            { "==",   Operator.Equals },
            { "!=",   Operator.NotEquals },
            { "<=",   Operator.LessThanOrEqualTo },
            { "<",    Operator.LessThan },
            { ">=",   Operator.GreaterThanOrEqualTo },
            { ">",    Operator.GreaterThan },

            { "(",    Operator.LeftParen },
            { ")",    Operator.RightParen },

            { "and",  Operator.LogicalAnd },
            { "or",   Operator.LogicalOr },

            { "<<",   Operator.ArrayAppend },
            { "=",    Operator.Assignment },
            { "+=",   Operator.AdditionAssignment },
            { "-=",   Operator.SubtractionAssignment },
            { "*=",   Operator.MultiplicationAssignment },
            { "/=",   Operator.DivisionAssignment },
        };

        public static Operator ToOperator(this string value)
        {
            if (OperatorMap.TryGetValue(value, out var op))
                return op;

            // Handle ":: new" with variable whitespace from the lexer
            var trimmed = value.Trim();
            if (trimmed == ":: new" || (trimmed.StartsWith("::") && trimmed.EndsWith("new")))
                return Operator.NestedClassInstance;

            throw new System.Exception($"Cannot parse '{value}' to an operator");
        }

        public static UnaryOperatorExpression ToUnaryExpression(this Operator op, WarScriptLanguage script, IExpression operand) =>
            op switch
            {
                Operator.Not           => new NotOperator(script, operand),
                Operator.ClassInstance => new ClassInstanceOperator(script, operand),
                Operator.Negate        => new NegateOperator(script, operand),
                _ => throw new System.Exception($"Operator {op} is not a unary operator")
            };

        public static BinaryOperatorExpression ToBinaryExpression(this Operator op, WarScriptLanguage script, IExpression left, IExpression right) =>
            op switch
            {
                Operator.Addition             => new AdditionOperator(script, left, right),
                Operator.Subtraction          => new SubtractionOperator(script, left, right),
                Operator.Multiplication       => new MultiplicationOperator(script, left, right),
                Operator.Division             => new DivisionOperator(script, left, right),
                Operator.Modulo               => new ModuloOperator(script, left, right),
                Operator.Equals               => new EqualsOperator(script, left, right),
                Operator.NotEquals            => new NotEqualsOperator(script, left, right),
                Operator.LessThan             => new LessThanOperator(script, left, right),
                Operator.LessThanOrEqualTo    => new LessThanOrEqualToOperator(script, left, right),
                Operator.GreaterThan          => new GreaterThanOperator(script, left, right),
                Operator.GreaterThanOrEqualTo => new GreaterThanOrEqualToOperator(script, left, right),
                Operator.LogicalAnd           => new LogicalAndOperator(script, left, right),
                Operator.LogicalOr            => new LogicalOrOperator(script, left, right),
                Operator.Assignment           => new AssignmentOperator(script, left, right),
                Operator.ClassProperty        => new ClassPropertyOperator(script, left, right),
                Operator.ClassCast            => new ClassCastOperator(script, left, right),
                Operator.ClassInstanceOf      => new ClassInstanceOfOperator(script, left, right),
                Operator.NestedClassInstance  => new NestedClassInstanceOperator(script, left, right),
                Operator.ArrayAppend          => new ArrayAppendOperator(script, left, right),
                Operator.ArrayValue           => new ArrayValueOperator(script, left, right),
                Operator.AdditionAssignment       => new AssignmentOperator(script, left,
                    new AdditionOperator(script, left, right)),
                Operator.SubtractionAssignment    => new AssignmentOperator(script, left,
                    new SubtractionOperator(script, left, right)),
                Operator.MultiplicationAssignment => new AssignmentOperator(script, left,
                    new MultiplicationOperator(script, left, right)),
                Operator.DivisionAssignment       => new AssignmentOperator(script, left,
                    new DivisionOperator(script, left, right)),
                _ => throw new System.Exception($"Operator {op} is not a binary operator")
            };

        public static bool IsBinary(this Operator op) => !IsUnary(op);

        public static bool IsUnary(this Operator op) =>
            op is Operator.Not || op is Operator.ClassInstance || op is Operator.Negate;

        public static int GetPrecedence(this Operator op) =>
            op switch
            {
                Operator.Not                  => 7,
                Operator.Negate               => 7,
                Operator.ClassInstance        => 7,
                Operator.NestedClassInstance  => 7,
                Operator.ClassProperty        => 7,
                Operator.ClassCast            => 7,
                Operator.ClassInstanceOf      => 7,
                Operator.ArrayValue           => 7,

                Operator.Multiplication       => 6,
                Operator.Division             => 6,
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
                Operator.AdditionAssignment       => 0,
                Operator.SubtractionAssignment    => 0,
                Operator.MultiplicationAssignment => 0,
                Operator.DivisionAssignment       => 0,

                _ => throw new System.Exception($"Operator {op} has no defined precedence")
            };

        public static bool GreaterThan(this Operator op, Operator other) =>
            op.GetPrecedence().CompareTo(other.GetPrecedence()) >= 0;
    }
}