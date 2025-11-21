using System;
using System.Collections.Generic;
using System.Linq;
using WarScript.Lexer;
using WarScript.Syntax.Operator;
using WarScript.Syntax.Statement;
using WarScript.Syntax.Types;

namespace WarScript.Syntax
{
    public class StatementParser
    {
        private readonly List<Token> _tokens;
        private int _position;

        private Dictionary<string, IValue> _variables;
        private Dictionary<string, StructureDefinition> _structures;

        public StatementParser(List<Token> tokens)
        {
            _tokens = tokens;
            _variables = new Dictionary<string, IValue>();
            _structures = new Dictionary<string, StructureDefinition>();
        }

        public IStatement Parse()
        {
            var root = new CompositeStatement();

            while (_position < _tokens.Count)
            {
                var statement = ParseExpression();
                root.AddStatement(statement);
            }
            
            return root;
        }

        private IStatement ParseExpression()
        {
            var token = Next(new List<TokenType>() { TokenType.Variable, TokenType.Keyword });
            switch (token.Type)
            {
                case TokenType.Variable:
                    // Skip equals
                    Next(new List<TokenType>() { TokenType.Operator }, "=");

                    IExpression value;
                    
                    // Check if the next token wants to create a new structure instance
                    if (Peek(TokenType.Keyword, "new"))
                        value = ReadInstance();
                    else
                        value = ReadExpression();

                    return new AssignStatement(
                        token.Value,
                        value,
                        (name, v) => _variables[name] = v);
                
                case TokenType.Keyword:
                    switch (token.Value)
                    {
                        case "print":
                            var expression = ReadExpression();
                            return new PrintStatement(expression);
                            
                        case "if":
                            // Read condition that will be used to evaluate IF
                            var condition = ReadExpression();
                            
                            // Skip start token
                            Next(new List<TokenType>() { TokenType.Keyword }, "then");

                            var conditionStatement = new ConditionStatement(condition);
                            // Keep reading until END keyword
                            while (!Peek(TokenType.Keyword, "end"))
                            {
                                var statement = ParseExpression();
                                conditionStatement.AddStatement(statement);
                            }
                            
                            // Skip end token
                            Next(new List<TokenType>() { TokenType.Keyword }, "end");

                            return conditionStatement;
                            
                        case "struct":
                            var type = Next(new List<TokenType>() { TokenType.Variable });

                            // Build up the argument list by reading to the END
                            var args = new HashSet<string>();
                            while (!Peek(TokenType.Keyword, "end"))
                            {
                                Next(new List<TokenType>() { TokenType.Keyword }, "arg");

                                var arg = Next(new List<TokenType>() { TokenType.Variable });
                                args.Add(arg.Value);
                            }
                            
                            // Skip end token
                            Next(new List<TokenType>() { TokenType.Keyword }, "end");

                            // Check that the structure is not already defined
                            if (_structures.ContainsKey(type.Value))
                            {
                                throw new Exception($"Structure {type.Value} is already defined");
                            }
                            else
                            {
                                var arguments = new List<string>();
                                arguments.AddRange(args);
                                _structures.Add(type.Value, new StructureDefinition(type.Value, arguments));
                            }
                            
                            return null;
                    }

                    break;
                
                default:
                    throw new Exception($"Statement can't start with the following lexeme {token}");
            }

            return null;
        }

        private IExpression ReadInstance()
        {
            // Skip the start token
            Next(new List<TokenType>() { TokenType.Keyword }, "new");

            var type = Next(new List<TokenType>() { TokenType.Variable });

            var arguments = new List<IExpression>();

            // Check if there will be a group of arguments
            if (Peek(TokenType.GroupDivider, "["))
            {
                // Skip the opening token
                Next(new List<TokenType>() { TokenType.GroupDivider }, "[");

                // Keep reading arguments until the group is closed
                while (!Peek(TokenType.GroupDivider, "]"))
                {
                    var value = ReadExpression();
                    arguments.Add(value);
                }
                
                // Skip the closing token
                Next(new List<TokenType>() { TokenType.GroupDivider }, "]");
            }

            if (_structures.TryGetValue(type.Value, out var definition))
            {
                return new StructureExpression(
                    definition,
                    arguments,
                    (name) => _variables.TryGetValue(name, out var v) ? v : null);
            }
            else
            {
                throw new Exception($"Structure is not defined: {type.Value}");
            }
        }
        
        private IExpression ReadExpression()
        {
            var left = NextExpression();

            while (Peek(TokenType.Operator))
            {
                var operation = Next(new List<TokenType>() { TokenType.Operator });
                var operatorType = operation.Value.ToOperator();

                if (operatorType.HasValue)
                {
                    if (operatorType.Value.SupportsTwoOperands())
                    {
                        var right = NextExpression();
                        left = operatorType.Value.ToOperatorExpression(left, right);
                    }
                    else
                    {
                        left = operatorType.Value.ToOperatorExpression(left);
                    }
                }
            }
            
            return left;
        }
        
        private IExpression NextExpression()
        {
            var token = Next(new List<TokenType>()
            {
                TokenType.Variable,
                TokenType.Numeric,
                TokenType.Logical,
                TokenType.Text
            });
            
            var value = token.Value;
            switch (token.Type)
            {
                case TokenType.Numeric:
                    return new NumericValue(int.Parse(value));
                case TokenType.Logical:
                    return new LogicalValue(bool.Parse(value));
                case TokenType.Text:
                    return new TextValue(value);
                case TokenType.Variable:
                default:
                    return new VariableExpression(
                        value,
                        (name) => _variables.TryGetValue(name, out var v) ? v : null);
            }
        }

        private Token Next(List<TokenType> types, string expectedValue = "")
        {
            var tokenTypes = new List<TokenType>();
            tokenTypes.AddRange(types);

            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];

                if (tokenTypes.Any(t => t == token.Type && (string.IsNullOrEmpty(expectedValue) || token.Value.Equals(expectedValue))))
                {
                    _position++;
                    return token;
                }
            }

            var previousToken = _tokens.ElementAtOrDefault(_position - 1);

            var expectedType = "";
            for (var i = 0; i < types.Count; i++)
            {
                expectedType += types[i].ToString();

                if (i < types.Count - 1)
                    expectedType += ", ";
            }
            
            throw new Exception($"After {previousToken} declaration expected any of the following lexemes: {expectedType}");
        }

        private bool Peek(TokenType type)
        {
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                return token.Type == type;
            }
            
            return false;
        }
        private bool Peek(TokenType type, string value)
        {
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                return token.Type == type && token.Value.Equals(value);
            }
            
            return false;
        }
    }
}