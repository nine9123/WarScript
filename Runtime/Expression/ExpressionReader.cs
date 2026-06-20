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
                    return token.Value == "{";   // array literal
                case TokenType.Keyword:
                    return token.Value == "fun";  // lambda in expression position
                default:
                    return false;
            }
        }

        private IExpression ReadExpression()
        {
            var lastWasOperand = false;

            while (HasNextToken())
            {
                var token = Tokens.Next();
                switch (token.Type)
                {
                    case TokenType.Operator:
                        var op = token.Value.ToOperator();

                        if (op == Operator.Operator.Subtraction && !lastWasOperand)
                            op = Operator.Operator.Negate;

                        switch (op)
                        {
                            case Operator.Operator.LeftParen:
                                _operators.Push(op);
                                lastWasOperand = false;
                                break;
                            case Operator.Operator.RightParen:
                                while (_operators.Count > 0 && _operators.Peek() != Operator.Operator.LeftParen)
                                    ApplyTopOperator();
                                _operators.Pop();
                                lastWasOperand = true;
                                break;
                            default:
                                while (_operators.Count > 0
                                       && _operators.Peek() != Operator.Operator.LeftParen
                                       && _operators.Peek().GreaterThan(op))
                                    ApplyTopOperator();
                                _operators.Push(op);
                                lastWasOperand = false;
                                break;
                        }
                        break;

                    default:
                        var value = token.Value;
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
                                // allow indexing on string literals: "hello"{0}
                                if (Tokens.PeekSameLine(TokenType.GroupDivider, "{"))
                                {
                                    Tokens.Next(TokenType.GroupDivider, "{");
                                    var textIndex = ReadExpression(_script, this);
                                    Tokens.Next(TokenType.GroupDivider, "}");
                                    operand = new ArrayValueOperator(_script, operand, textIndex);
                                }
                                break;
                            case TokenType.GroupDivider when token.Value == "{":
                                operand = ReadArrayInstance();
                                break;
                            case TokenType.Null:
                                operand = _script.NullExpr;
                                break;
                            case TokenType.This:
                                operand = _script.ThisExpr;
                                break;
                            case TokenType.Variable:
                            default:
                                var classInstanceOps = new List<Operator.Operator>
                                    { Operator.Operator.ClassInstance, Operator.Operator.NestedClassInstance };
                                if (_operators.Count > 0 && classInstanceOps.Contains(_operators.Peek()))
                                    operand = ReadClassInstance(token);
                                else if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
                                    operand = ReadFunctionInvocation(token);
                                else if (Tokens.PeekSameLine(TokenType.GroupDivider, "{"))
                                    operand = ReadArrayValue(token);
                                else
                                    operand = new VariableExpression(_script, value);
                                break;
                        }
                        _operands.Push(operand);
                        lastWasOperand = true;
                        break;
                }
            }

            while (_operators.Count > 0)
                ApplyTopOperator();

            return _operands.Count == 0 ? _script.NullExpr : _operands.Pop();
        }

        private void ApplyTopOperator()
        {
            if (_operators.Count == 0)
                throw new SyntaxException("Malformed expression: unexpected operator (missing operand?)");

            var op = _operators.Pop();

            if (_operands.Count == 0)
                throw new SyntaxException($"Malformed expression: operator '{op}' has no operands");

            var left = _operands.Pop();

            if (op.IsBinary())
            {
                if (_operands.Count == 0)
                    throw new SyntaxException($"Malformed expression: binary operator '{op}' missing left-hand operand");

                var right = _operands.Pop();
                _operands.Push(op.ToBinaryExpression(_script, right, left));
            }
            else
            {
                _operands.Push(op.ToUnaryExpression(_script, left));
            }
        }

        private ClassExpression ReadClassInstance(Token.Token token)
        {
            var properties = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "[");
                while (!Tokens.Peek(TokenType.GroupDivider, "]"))
                {
                    properties.Add(ReadExpression(_script, this));
                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }
                Tokens.Next(TokenType.GroupDivider, "]");
            }
            return new ClassExpression(_script, token.Value, properties);
        }

        private FunctionExpression ReadFunctionInvocation(Token.Token token)
        {
            var arguments = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "[");

                // Detect whether arguments are named (first non-whitespace arg is `name:`)
                // Allocated lazily — only if an argument turns out to be named.
                // The common case (positional args) never touches this dictionary.
                Dictionary<string, IExpression>? namedArgs = null;
                var isNamed = false;
                var checkedNaming = false;

                while (!Tokens.Peek(TokenType.GroupDivider, "]"))
                {
                    // Trailing comma guard: if the next real token is ], stop
                    if (Tokens.Peek(TokenType.GroupDivider, "]"))
                        break;

                    if (!checkedNaming)
                    {
                        // Peek ahead: if pattern is Variable followed by GroupDivider ":", it's named
                        isNamed = IsNamedArgumentAhead();
                        checkedNaming = true;
                    }

                    if (isNamed)
                    {
                        var argName = Tokens.Next(TokenType.Variable).Value;
                        Tokens.Next(TokenType.GroupDivider, ":");
                        var argExpr = ReadExpression(_script, this);
                        (namedArgs ??= new Dictionary<string, IExpression>())[argName] = argExpr;
                    }
                    else
                    {
                        arguments.Add(ReadExpression(_script, this));
                    }

                    // Consume comma, then re-check for trailing comma before ]
                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }
                Tokens.Next(TokenType.GroupDivider, "]");

                // Reorder named args to match declared parameter positions
                if (isNamed && namedArgs is { Count: > 0 })
                {
                    var definition = _script.DefinitionContext.GetScope()
                        .GetFunction(token.Value, namedArgs.Count);

                    if (definition != null)
                    {
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
            return new FunctionExpression(_script, token.Value, arguments);
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
            while (!Tokens.Peek(TokenType.GroupDivider, "}"))
            {
                values.Add(ReadExpression(_script, this));
                if (Tokens.Peek(TokenType.GroupDivider, ","))
                    Tokens.Next();
            }
            Tokens.Next(TokenType.GroupDivider, "}");
            return new ArrayExpression(_script, values);
        }

        private ArrayValueOperator ReadArrayValue(Token.Token token)
        {
            var array = new VariableExpression(_script, token.Value);
            Tokens.Next(TokenType.GroupDivider, "{");
            var arrayIndex = ReadExpression(_script, this);
            Tokens.Next(TokenType.GroupDivider, "}");
            return new ArrayValueOperator(_script, array, arrayIndex);
        }

        /// <summary>
        /// Parse a lambda expression: <c>fun [params] body end</c>.
        /// The <c>fun</c> keyword has already been consumed.
        /// </summary>
        private LambdaExpression ReadLambda(Token.Token funToken)
        {
            var parameters = new List<string>();

            // Parse parameter list
            Tokens.Next(TokenType.GroupDivider, "[");
            while (!Tokens.Peek(TokenType.GroupDivider, "]"))
            {
                var param = Tokens.Next(TokenType.Variable);
                parameters.Add(param.Value);
                if (Tokens.Peek(TokenType.GroupDivider, ","))
                    Tokens.Next();
            }
            Tokens.Next(TokenType.GroupDivider, "]");

            // Parse body statements until 'end'
            var body = new FunctionStatement(_script, funToken.RowNumber, "<lambda>");
            var bodyScope = _script.DefinitionContext.NewScope();
            StatementParser.ParseLambdaBody(_script, Tokens, body, bodyScope);

            return new LambdaExpression(_script, parameters, body, funToken.RowNumber);
        }
    }
}