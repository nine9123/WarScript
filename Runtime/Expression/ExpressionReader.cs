using System.Collections.Generic;
using WarScript.Expression.Operator;
using WarScript.Expression.Operator.Extensions;
using WarScript.Expression.Value;
using WarScript.Token;

namespace WarScript.Expression
{
    public class ExpressionReader
    {
        private readonly Stack<IExpression> _operands;
        private readonly Stack<Operator.Operator> _operators;
        public TokensStack Tokens { get; }

        private WarScriptLanguage _script;
        
        private ExpressionReader(WarScriptLanguage script, TokensStack tokens)
        {
            _script = script;
            _operands = new Stack<IExpression>();
            _operators = new Stack<Operator.Operator>();
            Tokens = tokens;
        }

        public static IExpression ReadExpression(WarScriptLanguage script, TokensStack tokens)
        {
            var reader = new ExpressionReader(script, tokens);
            return reader.ReadExpression();
        }

        public static IExpression ReadExpression(WarScriptLanguage script, ExpressionReader reader)
        {
            return ReadExpression(script, reader.Tokens);
        }

        private bool HasNextToken()
        {
            if (Tokens.PeekSameLine(TokenType.Operator, TokenType.Variable, TokenType.Numeric,
                    TokenType.Logical, TokenType.Null, TokenType.This, TokenType.Text))
                return true;
            // beginning of an array
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "{"))
                return true;
            return false;
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
                        
                        // '-' after an operand is subtraction, otherwise it's negation
                        if (op == Operator.Operator.Subtraction && !lastWasOperand)
                            op = Operator.Operator.Negate;
                        
                        switch (op)
                        {
                            case Operator.Operator.LeftParen:
                                _operators.Push(op);
                                lastWasOperand = false;
                                break;
                            case Operator.Operator.RightParen:
                                // until left bracket is not reached
                                while (_operators.Count > 0 && _operators.Peek() != Operator.Operator.LeftParen)
                                    ApplyTopOperator();
                                _operators.Pop(); // pop left bracket
                                lastWasOperand = true; // (...) acts as an operand
                                break;
                            default:
                                // until top operator has greater or equal precedence
                                // Never pop past a LeftParen — it is only removed by
                                // the matching RightParen case above.
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
                            case TokenType.Numeric:
                                operand = _script.GetNumeric(double.Parse(value));
                                break;
                            case TokenType.Logical:
                                operand = bool.Parse(value) ? _script.LogicalTrue : _script.LogicalFalse;
                                break;
                            case TokenType.Text:
                                operand = new TextValue(_script, value);
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
                                operand = _script.Null;
                                break;
                            case TokenType.This:
                                operand = _script.This;
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

            return _operands.Count == 0 ? _script.Null : _operands.Pop();
        }
        
        private void ApplyTopOperator()
        {
            var op = _operators.Pop();
            var left = _operands.Pop();

            if (op.IsBinary())
            {
                var right = _operands.Pop();
                _operands.Push(op.ToBinaryExpression(_script, right, left));
            }
            else
            {
                _operands.Push(op.ToUnaryExpression(_script, left));
            }
        }

        // read class instance: new Class [ property1, property2, ... ]
        private ClassExpression ReadClassInstance(Token.Token token)
        {
            var properties = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "[");

                while (!Tokens.PeekSameLine(TokenType.GroupDivider, "]"))
                {
                    properties.Add(ReadExpression(_script, this));
                    if (Tokens.PeekSameLine(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }

                Tokens.Next(TokenType.GroupDivider, "]");
            }
            return new ClassExpression(_script, token.Value, properties);
        }

        // read function invocation: function_name [ argument1, argument2 ]
        private FunctionExpression ReadFunctionInvocation(Token.Token token)
        {
            var arguments = new List<IExpression>();
            if (Tokens.PeekSameLine(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "[");

                while (!Tokens.PeekSameLine(TokenType.GroupDivider, "]"))
                {
                    arguments.Add(ReadExpression(_script, this));
                    if (Tokens.PeekSameLine(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }

                Tokens.Next(TokenType.GroupDivider, "]");
            }
            return new FunctionExpression(_script, token.Value, arguments);
        }

        // read array instantiation: array = {1,2,3}
        private ArrayExpression ReadArrayInstance()
        {
            var values = new List<IExpression>();

            while (!Tokens.PeekSameLine(TokenType.GroupDivider, "}"))
            {
                values.Add(ReadExpression(_script, this));
                if (Tokens.PeekSameLine(TokenType.GroupDivider, ","))
                    Tokens.Next();
            }

            Tokens.Next(TokenType.GroupDivider, "}");
            return new ArrayExpression(_script, values);
        }

        // read array value: array{index}
        private ArrayValueOperator ReadArrayValue(Token.Token token)
        {
            var array = new VariableExpression(_script, token.Value);
            Tokens.Next(TokenType.GroupDivider, "{");
            var arrayIndex = ReadExpression(_script, this);
            Tokens.Next(TokenType.GroupDivider, "}");
            return new ArrayValueOperator(_script, array, arrayIndex);
        }
    }
}