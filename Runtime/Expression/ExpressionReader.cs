using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression.Operator;
using WarScript.Expression.Operator.Extensions;
using WarScript.Expression.Value;
using WarScript.Parser;
using WarScript.Statement;
using WarScript.Token;

namespace WarScript.Expression
{
    public class ExpressionReader
    {
        private readonly Stack<IExpression> _operands;
        private readonly Stack<Operator.Operator> _operators;

        /// <summary>
        /// The token of each operator on <see cref="_operators"/>, in step
        /// with it: the expression an operator builds, and errors about it,
        /// are positioned there (T3.1).
        /// </summary>
        private readonly Stack<Token.Token> _operatorTokens;

        /// <summary>
        /// The token-list index of each operator on <see cref="_operators"/>,
        /// in step with it, for <see cref="WarScriptLanguage.ExpressionSpans"/>.
        /// </summary>
        private readonly Stack<int> _operatorIndexes;
        public TokensStack Tokens { get; private set; }

        private WarScriptLanguage _script;

        // Reader pool. ReadExpression recurses once per nested sub-expression
        // (function args, parens, array/class literals), and each level used to
        // allocate a fresh reader plus two Stacks. Pooling reuses a handful of
        // readers — one per active recursion level — across the whole parse.
        // ThreadStatic so parses on different threads stay isolated; the
        // depth-first usage makes the per-thread pool behave like a stack.
        [System.ThreadStatic] private static Stack<ExpressionReader>? _pool;

        private ExpressionReader(WarScriptLanguage script, TokensStack tokens)
        {
            _script = script;
            _operands = new Stack<IExpression>();
            _operators = new Stack<Operator.Operator>();
            _operatorTokens = new Stack<Token.Token>();
            _operatorIndexes = new Stack<int>();
            Tokens = tokens;
        }

        public static IExpression ReadExpression(WarScriptLanguage script, TokensStack tokens)
        {
            var reader = Rent(script, tokens);
            try
            {
                return reader.ReadExpression();
            }
            finally
            {
                Return(reader);
            }
        }

        public static IExpression ReadExpression(WarScriptLanguage script, ExpressionReader reader)
        {
            return ReadExpression(script, reader.Tokens);
        }

        private static ExpressionReader Rent(WarScriptLanguage script, TokensStack tokens)
        {
            var pool = _pool;
            if (pool != null && pool.Count > 0)
            {
                var reader = pool.Pop(); // its stacks were cleared on Return
                reader._script = script;
                reader.Tokens = tokens;
                return reader;
            }
            return new ExpressionReader(script, tokens);
        }

        private static void Return(ExpressionReader reader)
        {
            // Release references and guarantee empty stacks for the next rent,
            // even if ReadExpression threw mid-parse.
            reader._operands.Clear();
            reader._operators.Clear();
            reader._operatorTokens.Clear();
            reader._operatorIndexes.Clear();
            reader._script = null!;
            reader.Tokens = null!;
            (_pool ??= new Stack<ExpressionReader>()).Push(reader);
        }

        private bool HasNextToken()
        {
            // Single same-line peek + switch — avoids allocating a params TokenType[]
            // on every call and avoids making up to three PeekSameLine calls. Must NOT
            // skip empty tokens here: a line break ends the expression.
            if (!Tokens.TryPeekSameLine(out var token))
                return false;
            switch (token.Type)
            {
                case TokenType.Operator:
                case TokenType.Variable:
                case TokenType.Numeric:
                case TokenType.Logical:
                case TokenType.Null:
                case TokenType.This:
                case TokenType.Text:
                    return true;
                case TokenType.GroupDivider:
                    return token.Value == "[";   // array literal, or an index after an operand
                case TokenType.Keyword:
                    return token.Value == "fun";  // lambda in expression position
                default:
                    return false;
            }
        }

        private IExpression ReadExpression()
        {
            var lastWasOperand = false;
            var lastRow = 0;

            while (HasNextToken())
            {
                var token = Tokens.Next();
                lastRow = token.RowNumber;
                switch (token.Type)
                {
                    case TokenType.Operator:
                        var op = token.Value.ToOperator();

                        if (op == Operator.Operator.Subtraction && !lastWasOperand)
                            op = Operator.Operator.Negate;

                        switch (op)
                        {
                            case Operator.Operator.LeftParen:
                                // A '(' after an operand is not a call (the lexer
                                // made it a grouping one): `f(x)(y)`, `arr[0](y)`,
                                // `x (1)` after a literal. Pushing it would drop the
                                // operand on its left without a word (T3.12 review).
                                if (lastWasOperand)
                                    throw new SyntaxException(
                                        System.FormattableString.Invariant($"Unexpected `(` at line {token.RowNumber}: expected an operator or end of expression") +
                                        " (only a name can be called: put a call's or an element's result in a variable first)",
                                        token.RowNumber, token.Column);
                                _operators.Push(op);
                                _operatorTokens.Push(token);
                                _operatorIndexes.Push(Tokens.Position - 1);
                                lastWasOperand = false;
                                break;
                            case Operator.Operator.RightParen:
                                while (_operators.Count > 0 && _operators.Peek() != Operator.Operator.LeftParen)
                                    ApplyTopOperator();
                                if (_operators.Count == 0)
                                    throw new SyntaxException(
                                        System.FormattableString.Invariant($"Unbalanced ')' at line {token.RowNumber}"), token.RowNumber, token.Column);
                                _operators.Pop();
                                _operatorTokens.Pop();
                                var leftParen = _operatorIndexes.Pop();
                                // A parenthesized operand spans its parentheses.
                                if (_operands.Count > 0)
                                    Span(_operands.Peek(), leftParen, Tokens.Position - 1);
                                lastWasOperand = true;
                                break;
                            default:
                                while (_operators.Count > 0
                                       && _operators.Peek() != Operator.Operator.LeftParen
                                       && _operators.Peek().GreaterThan(op))
                                    ApplyTopOperator();
                                _operators.Push(op);
                                _operatorTokens.Push(token);
                                _operatorIndexes.Push(Tokens.Position - 1);
                                lastWasOperand = false;
                                break;
                        }
                        break;

                    default:
                        // `[` after an operand indexes it (T3.12): `grid[0][1]`,
                        // `f(x)[0]`, `(a + b)[0]`, `obj.items[0][1]`. (A name's
                        // own first index is read with the name, below.)
                        if (lastWasOperand && token.Type == TokenType.GroupDivider && token.Value == "[")
                        {
                            ReadPostfixIndex(token);
                            break;
                        }

                        // Two operands in a row means an operator is missing
                        // (`x = 1 2`, `print a b`). Without this check the first
                        // operand would be silently discarded.
                        if (lastWasOperand)
                            throw new SyntaxException(
                                System.FormattableString.Invariant($"Unexpected `{token.Value}` at line {token.RowNumber}: expected an operator or end of expression"), token.RowNumber, token.Column);

                        var value = token.Value;
                        var first = Tokens.Position - 1;
                        IExpression operand;
                        switch (token.Type)
                        {
                            case TokenType.Keyword when token.Value == "fun":
                                operand = ReadLambda(token);
                                break;
                            case TokenType.Numeric:
                                operand = new ConstantExpression(WarValue.FromNumeric(NumericLiteral.Parse(value)));
                                break;
                            case TokenType.Logical:
                                operand = bool.Parse(value) ? _script.TrueExpr : _script.FalseExpr;
                                break;
                            case TokenType.Text:
                                operand = new ConstantExpression(WarValue.FromText(value));
                                break;
                            case TokenType.GroupDivider when token.Value == "[":
                                operand = ReadArrayInstance();
                                break;
                            case TokenType.Null:
                                // Not NullExpr: that stands for an empty expression, which
                                // a block header rejects (StatementParser.RequireExpression).
                                operand = new ConstantExpression(WarValue.Null);
                                break;
                            case TokenType.This:
                                // A lambda has no `this` (T3.3, PROP-16); `@x` is lexed as `this.x`.
                                if (_script.ParsingBody == ParseBody.Lambda && !_script.ParsingV1)
                                    throw new SyntaxException("'this' and '@' fields cannot be used in a lambda", token.RowNumber, token.Column);
                                // Nor has code outside every class (T3.8, PROP-29).
                                if (_script.ParsingClassDepth == 0 && !_script.ParsingV1)
                                    throw new SyntaxException("'this' and '@' fields can only be used inside a class", token.RowNumber, token.Column);
                                operand = _script.ThisExpr;
                                break;
                            case TokenType.Variable:
                            default:
                                var classInstanceOps = new List<Operator.Operator>
                                    { Operator.Operator.ClassInstance, Operator.Operator.NestedClassInstance };
                                if (_operators.Count > 0 && classInstanceOps.Contains(_operators.Peek()))
                                    operand = ReadClassInstance(token);
                                else if (Tokens.PeekSameLine(TokenType.GroupDivider, "("))
                                    operand = ReadFunctionInvocation(token);
                                else if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
                                    operand = ReadArrayValue(token);
                                else
                                    operand = new VariableExpression(_script, value) { Line = token.RowNumber, Column = token.Column };
                                break;
                        }
                        Span(operand, first, Tokens.Position - 1);
                        _operands.Push(operand);
                        lastWasOperand = true;
                        break;
                }
            }

            while (_operators.Count > 0)
            {
                // A '(' still open when the expression ends (`x = (1]`) is
                // not an operator to apply (CRASH-6).
                if (_operators.Peek() == Operator.Operator.LeftParen)
                    throw new SyntaxException(
                        System.FormattableString.Invariant($"Unbalanced '(' at line {lastRow}: missing ')'"),
                        _operatorTokens.Peek().RowNumber, _operatorTokens.Peek().Column);
                ApplyTopOperator();
            }

            return _operands.Count == 0 ? _script.NullExpr : _operands.Pop();
        }

        private void ApplyTopOperator()
        {
            if (_operators.Count == 0)
                throw new SyntaxException("Malformed expression: unexpected operator (missing operand?)");

            var op = _operators.Pop();
            var token = _operatorTokens.Pop();
            var index = _operatorIndexes.Pop();

            if (_operands.Count == 0)
                throw new SyntaxException($"Malformed expression: operator '{op}' has no operands", token.RowNumber, token.Column);

            var left = _operands.Pop();

            if (op.IsBinary())
            {
                if (_operands.Count == 0)
                    throw new SyntaxException($"Malformed expression: binary operator '{op}' missing left-hand operand", token.RowNumber, token.Column);

                // The names are swapped: `left` was popped first, so it is the
                // right-hand operand, and `right` the left-hand one.
                var right = _operands.Pop();
                var binary = At(op.ToBinaryExpression(_script, right, left), token);
                var spans = _script.ExpressionSpans;
                if (spans != null && spans.TryGetValue(right, out var leftSpan) && spans.TryGetValue(left, out var rightSpan))
                    Span(binary, leftSpan.First, rightSpan.Last);
                _operands.Push(binary);
            }
            else
            {
                // Every unary operator is a prefix: it spans from its token.
                var unary = At(op.ToUnaryExpression(_script, left), token);
                var spans = _script.ExpressionSpans;
                if (spans != null && spans.TryGetValue(left, out var operandSpan))
                    Span(unary, index, operandSpan.Last);
                _operands.Push(unary);
            }
        }

        /// <summary>
        /// Records the tokens <paramref name="first"/> to <paramref name="last"/>
        /// (token-list indexes) as the source of <paramref name="expression"/>,
        /// when the instance records spans (the migration tool, T3.2).
        /// </summary>
        private void Span(IExpression expression, int first, int last)
        {
            var spans = _script.ExpressionSpans;
            if (spans != null) spans[expression] = new ExpressionSpan(first, last);
        }

        // Position an expression at a token (T3.1).
        private static BinaryOperatorExpression At(BinaryOperatorExpression e, Token.Token token)
        {
            e.Line = token.RowNumber;
            e.Column = token.Column;
            return e;
        }

        private static UnaryOperatorExpression At(UnaryOperatorExpression e, Token.Token token)
        {
            e.Line = token.RowNumber;
            e.Column = token.Column;
            return e;
        }

        private ClassExpression ReadClassInstance(Token.Token token)
        {
            var properties = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "("))
            {
                Tokens.Next(TokenType.GroupDivider, "(");
                while (!Tokens.Peek(TokenType.GroupDivider, ")"))
                {
                    var before = Tokens.Position;
                    properties.Add(ReadExpression(_script, this));
                    RequireProgress(before, ")");
                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }
                Tokens.Next(TokenType.GroupDivider, ")");
            }
            return new ClassExpression(_script, token.Value, properties) { Line = token.RowNumber, Column = token.Column };
        }

        private FunctionExpression ReadFunctionInvocation(Token.Token token)
        {
            var arguments = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "("))
            {
                Tokens.Next(TokenType.GroupDivider, "(");

                // Detect whether arguments are named (first non-whitespace arg is `name:`)
                // Allocated lazily — only if an argument turns out to be named.
                // The common case (positional args) never touches this dictionary.
                Dictionary<string, IExpression>? namedArgs = null;
                List<Token.Token>? namedTokens = null;
                var isNamed = false;
                var checkedNaming = false;

                while (!Tokens.Peek(TokenType.GroupDivider, ")"))
                {
                    // Trailing comma guard: if the next real token is ], stop
                    if (Tokens.Peek(TokenType.GroupDivider, ")"))
                        break;

                    if (!checkedNaming)
                    {
                        // Peek ahead: if pattern is Variable followed by GroupDivider ":", it's named
                        isNamed = IsNamedArgumentAhead();
                        checkedNaming = true;
                    }

                    var before = Tokens.Position;
                    if (isNamed)
                    {
                        var argToken = Tokens.Next(TokenType.Variable);
                        var argName = argToken.Value;
                        Tokens.Next(TokenType.GroupDivider, ":");
                        var argExpr = ReadExpression(_script, this);
                        namedArgs ??= new Dictionary<string, IExpression>();
                        // Each name once (T3.8, PROP-29).
                        if (namedArgs.ContainsKey(argName) && !_script.ParsingV1)
                            throw new SyntaxException("Argument '" + argName + "' is given twice", argToken.RowNumber, argToken.Column);
                        namedArgs[argName] = argExpr;
                        (namedTokens ??= new List<Token.Token>()).Add(argToken);
                    }
                    else
                    {
                        arguments.Add(ReadExpression(_script, this));
                    }
                    RequireProgress(before, ")");

                    // Consume comma, then re-check for trailing comma before ]
                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }
                Tokens.Next(TokenType.GroupDivider, ")");

                // Reorder named args to match declared parameter positions
                if (isNamed && namedArgs is { Count: > 0 })
                {
                    var definition = _script.DefinitionContext.GetScope()
                        .GetFunction(token.Value, namedArgs.Count)
                        ?? _script.FindImportedFunction(token.Value, namedArgs.Count);

                    if (definition != null)
                    {
                        // Every name must be a parameter (T3.8, PROP-29):
                        // v1 dropped the argument.
                        if (!_script.ParsingV1)
                            foreach (var named in namedTokens!)
                                if (!definition.Details.Arguments.Contains(named.Value))
                                    throw new SyntaxException("Function '" + token.Value + "' has no parameter '" + named.Value + "'",
                                        named.RowNumber, named.Column);
                        foreach (var paramName in definition.Details.Arguments)
                        {
                            if (namedArgs.TryGetValue(paramName, out var expr))
                                arguments.Add(expr);
                            else
                                arguments.Add(_script.NullExpr);
                        }
                    }
                    else
                    {
                        // Definition not found yet (forward reference): preserve insertion order
                        foreach (var expr in namedArgs.Values)
                            arguments.Add(expr);
                    }
                }
            }
            return new FunctionExpression(_script, token.Value, arguments) { Line = token.RowNumber, Column = token.Column };
        }

        /// <summary>
        /// Peeks ahead (without consuming) to check if the next argument looks like
        /// a named argument: <c>identifier :</c>
        /// </summary>
        private bool IsNamedArgumentAhead()
        {
            // We need to look two tokens ahead: Variable then GroupDivider ":"
            // TokensStack only has single-token peek, so we read + back twice.
            if (!Tokens.Peek(TokenType.Variable))
                return false;

            var nameToken = Tokens.Next(); // consume variable
            bool hasColon = Tokens.PeekSameLine(TokenType.GroupDivider, ":");
            Tokens.Back();                 // put the variable back
            return hasColon;
        }

        private ArrayExpression ReadArrayInstance()
        {
            var values = new List<IExpression>();
            while (!Tokens.Peek(TokenType.GroupDivider, "]"))
            {
                var before = Tokens.Position;
                values.Add(ReadExpression(_script, this));
                RequireProgress(before, "]");
                if (Tokens.Peek(TokenType.GroupDivider, ","))
                    Tokens.Next();
            }
            Tokens.Next(TokenType.GroupDivider, "]");
            return new ArrayExpression(_script, values);
        }

        /// <summary>
        /// Guards the bracket-scanning loops against an element read that made
        /// no progress — at end of input, or facing a token that cannot start
        /// an expression, ReadExpression consumes nothing and the enclosing
        /// while-loop would spin forever.
        /// </summary>
        private void RequireProgress(int positionBefore, string closing)
        {
            if (Tokens.Position != positionBefore)
                return;

            if (Tokens.TryPeek(out var next))
                throw new SyntaxException(
                    System.FormattableString.Invariant($"Unexpected `{next.Value}` at line {next.RowNumber}: expected an expression, ',' or '{closing}'"), next.RowNumber, next.Column);

            throw new SyntaxException($"Unexpected end of script: expected '{closing}'");
        }

        private ArrayValueOperator ReadArrayValue(Token.Token token)
        {
            var array = new VariableExpression(_script, token.Value) { Line = token.RowNumber, Column = token.Column };
            var brace = Tokens.Next(TokenType.GroupDivider, "[");
            var arrayIndex = ReadExpression(_script, this);
            CloseIndex(token.Value);
            return (ArrayValueOperator)At(new ArrayValueOperator(_script, array, arrayIndex), brace);
        }

        /// <summary>
        /// Reads an index's closing <c>]</c>. A <c>,</c> instead is most likely
        /// the legacy call syntax, <c>f [a, b]</c> (T3.12): say how a call is
        /// written now rather than that a <c>]</c> was expected.
        /// </summary>
        private void CloseIndex(string name)
        {
            if (Tokens.TryPeek(out var next) && next.Type == TokenType.GroupDivider && next.Value == ",")
                throw new SyntaxException(
                    System.FormattableString.Invariant($"Unexpected ',' in an index at line {next.RowNumber}: an index takes one value")
                    + (name != null ? " (calls are written " + name + "(a, b))" : " (calls are written f(a, b))"),
                    next.RowNumber, next.Column);
            Tokens.Next(TokenType.GroupDivider, "]");
        }

        /// <summary>
        /// Reads <c>[index]</c> after a complete operand and replaces the
        /// operand on the stack with its element. The member operators still
        /// pending on its left bind tighter (<c>obj.items[0][1]</c> indexes
        /// <c>obj.items[0]</c>), so they are applied first; nothing else is.
        /// </summary>
        private void ReadPostfixIndex(Token.Token bracket)
        {
            var first = Tokens.Position - 1;
            while (_operators.Count > 0
                   && (_operators.Peek() == Operator.Operator.ClassProperty
                       || _operators.Peek() == Operator.Operator.NestedClassInstance
                       || _operators.Peek() == Operator.Operator.ClassInstance))
                ApplyTopOperator();

            var target = _operands.Pop();
            var index = ReadExpression(_script, this);
            CloseIndex(null);
            var element = At(new ArrayValueOperator(_script, target, index), bracket);
            var spans = _script.ExpressionSpans;
            Span(element, spans != null && spans.TryGetValue(target, out var targetSpan) ? targetSpan.First : first,
                Tokens.Position - 1);
            _operands.Push(element);
        }

        /// <summary>
        /// Parse a lambda expression: <c>fun(params) body end</c>.
        /// The <c>fun</c> keyword has already been consumed.
        /// </summary>
        private LambdaExpression ReadLambda(Token.Token funToken)
        {
            var parameters = new List<string>();

            // Parse parameter list
            StatementParser.RejectLegacyParameters(Tokens, funToken, "fun(...)");
            Tokens.Next(TokenType.GroupDivider, "(");
            while (!Tokens.Peek(TokenType.GroupDivider, ")"))
            {
                var param = Tokens.Next(TokenType.Variable);
                StatementParser.CheckParameter(_script, parameters, param);
                parameters.Add(param.Value);
                if (Tokens.Peek(TokenType.GroupDivider, ","))
                    Tokens.Next();
            }
            Tokens.Next(TokenType.GroupDivider, ")");

            // Parse body statements until 'end'
            var body = new FunctionStatement(_script, funToken.RowNumber, "<lambda>");
            var bodyScope = _script.DefinitionContext.NewScope();
            StatementParser.ParseLambdaBody(_script, Tokens, body, bodyScope);

            return new LambdaExpression(_script, parameters, body, funToken.RowNumber, funToken.Column);
        }
    }
}