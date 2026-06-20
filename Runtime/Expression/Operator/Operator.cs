namespace WarScript.Expression.Operator
{
    public enum Operator
    {
        // Precedence 7 - unary / class ops
        Not,
        Negate,
        ClassInstance,
        NestedClassInstance,
        ClassProperty,
        ClassCast,
        ClassInstanceOf,
        ArrayValue,

        // Precedence 6 - multiplicative
        Multiplication,
        Division,
        Modulo,

        // Precedence 5 - additive
        Addition,
        Subtraction,

        // Precedence 4 - comparison
        Equals,
        NotEquals,
        LessThan,
        LessThanOrEqualTo,
        GreaterThan,
        GreaterThanOrEqualTo,

        // Precedence 3 - parentheses
        LeftParen,
        RightParen,

        // Precedence 2-1 - logical
        LogicalAnd,
        LogicalOr,

        // Precedence 0 - lowest
        ArrayAppend,
        Assignment,
        AdditionAssignment,
        SubtractionAssignment,
        MultiplicationAssignment,
        DivisionAssignment,
    }
}