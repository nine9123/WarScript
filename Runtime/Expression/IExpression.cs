namespace WarScript.Expression
{
    /// <summary>
    /// An expression node of the AST. The AST is plain data: the compiler
    /// turns it into bytecode and nothing executes it (plan T2.1).
    /// </summary>
    public interface IExpression
    {
    }

    /// <summary>
    /// An expression that knows where it is in the source (T3.1): the line
    /// and column (from 1; 0 when unknown) of the token a runtime error in
    /// it is reported at: an operator, a function or class name, the
    /// <c>{</c> of an index. The compiler marks the instructions it emits
    /// for the expression with that column when it is on the line being
    /// compiled.
    /// </summary>
    public interface IPositionedExpression : IExpression
    {
        int Line { get; }
        int Column { get; }
    }
}

namespace WarScript.Expression
{
    /// <summary>
    /// The tokens an expression was read from: indexes into the lexer's
    /// token list, first and last inclusive; a parenthesized expression
    /// includes its parentheses. Recorded only into
    /// <see cref="WarScriptLanguage.ExpressionSpans"/>, for the migration
    /// tool (T3.2).
    /// </summary>
    internal readonly struct ExpressionSpan
    {
        public readonly int First;
        public readonly int Last;

        public ExpressionSpan(int first, int last)
        {
            First = first;
            Last = last;
        }
    }
}
