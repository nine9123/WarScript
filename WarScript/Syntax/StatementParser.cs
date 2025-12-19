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

        public readonly Dictionary<string, IValue> _variables;
        private readonly Dictionary<string, StructureDefinition> _structures;
        
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
                // Custom logic: Handle LineBreaks at EOF
                SkipLineBreaks();
                if (_position >= _tokens.Count)
                    break;
                
                var statement = ParseExpression();
                root.AddStatement(statement);
            }
            
            return root;
        }

        private IStatement ParseExpression()
        {
            var token = Next(new List<TokenType>() { TokenType.Keyword, TokenType.Variable, TokenType.Operator });
            switch (token.Type)
            {
                case TokenType.Variable:
                case TokenType.Operator:
                    _position--;
                    
                    var value = new ExpressionReader(this).ReadExpression();
                    if (value is AssigmentOperator assigmentOperator &&
                        assigmentOperator.Left is VariableExpression variableExpression)
                    {
                        return new AssigmentStatement(
                            variableExpression.Name,
                            assigmentOperator.Right,
                            (name, v) => _variables[name] = v);
                    }
                    else
                    {
                        throw new Exception($"Unsupported statement: {value}");
                    }
                case TokenType.Keyword:
                    switch (token.Value)
                    {
                        case "print":
                            var expression = new ExpressionReader(this).ReadExpression();
                            return new PrintStatement(expression);
                            
                        case "if":
                            // Read condition that will be used to evaluate IF
                            var condition = new ExpressionReader(this).ReadExpression();
                            
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

        public IExpression ReadInstance(Token token)
        {
            if (_structures.TryGetValue(token.Value, out var definition))
            {
                var arguments = new List<IExpression>();
                
                // Check if there will be a group of arguments
                if (Peek(TokenType.GroupDivider, "["))
                {
                    // Skip open square bracket
                    Next(new List<TokenType>() { TokenType.GroupDivider }, "[");

                    // Keep reading arguments until the group is closed
                    while (!Peek(TokenType.GroupDivider, "]"))
                    {
                        var value = new ExpressionReader(this).ReadExpression();
                        arguments.Add(value);

                        if (Peek(TokenType.GroupDivider, ","))
                            Next();
                    }
                
                    // Skip close square bracket
                    Next(new List<TokenType>() { TokenType.GroupDivider }, "]");
                }

                // Custom logic: to check argument count, might be removed later if tutorial has a better solution
                if (definition.Arguments.Count != arguments.Count)
                {
                    var expected = "Expected";
                    foreach (var definitionArgument in definition.Arguments)
                    {
                        expected += $"\n\t\t{definitionArgument}";
                    }

                    var got = "Got";
                    foreach (var argument in arguments)
                    {
                        got += $"\n\t\t{argument}";
                    }
                    
                    throw new Exception($"Line {token.Line}: Argument count does not match for structure {definition.Name}\n\t{expected}\n\t{got}");
                }
                
                return new StructureExpression(
                    definition,
                    arguments,
                    name => _variables.TryGetValue(name, out var v) ? v : null);
            }
            else
            {
                throw new Exception($"Structure is not defined: {token.Value}");
            }
        }
        
        private Token Next(List<TokenType> types, string expectedValue = "")
        {
            SkipLineBreaks();
            
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

        public Token Next()
        {
            SkipLineBreaks();

            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                _position++;
                
                return token;
            }

            return null;
        }

        public bool Peek(TokenType type)
        {
            SkipLineBreaks();
            
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                return token.Type == type;
            }
            
            return false;
        }
        
        private bool Peek(TokenType type, string value)
        {
            SkipLineBreaks();
            
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                return token.Type == type && token.Value.Equals(value);
            }
            
            return false;
        }
        
        private void SkipLineBreaks()
        {
            while (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                if (token.Type == TokenType.LineBreak)
                {
                    _position++;
                    continue;
                }

                break;
            }
        }
        
        public bool Peek(List<TokenType> types)
        {
            if (_position < _tokens.Count)
            {
                var token = _tokens[_position];
                return types.Contains(token.Type);
            }
            
            return false;
        }
    }
}