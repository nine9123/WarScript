using System.Collections.Generic;
using WarScript.Lexer;
using WarScript.Syntax.Operator;
using WarScript.Syntax.Types;

namespace WarScript.Syntax
{
    public class ExpressionReader
    {
        private StatementParser _statementParser;
        
        private Stack<IExpression> _operands = new Stack<IExpression>();
        private Stack<Operator.Operator> _operators = new Stack<Operator.Operator>();
        
        public ExpressionReader(StatementParser statementParser)
        {
            _statementParser = statementParser;
        }
        
        public IExpression ReadExpression()
        {
            var validTokens = new List<TokenType>()
            {
                TokenType.Operator,
                TokenType.Variable,
                TokenType.Numeric,
                TokenType.Logical,
                TokenType.Text
            };
            
            while (_statementParser.Peek(validTokens))
            {
                var token = _statementParser.Next();
                switch (token.Type)
                {
                    case TokenType.Operator:
                        var @operator = token.Value.ToOperator();
                        switch (@operator)
                        {
                            case Operator.Operator.LeftParen:
                                _operators.Push(@operator);
                                break;
                            case Operator.Operator.RightParen:
                                // Until left parenthesis is not reached
                                while (_operators.Count != 0 && _operators.Peek() != Operator.Operator.LeftParen)
                                    ApplyTopOperator();
                                _operators.Pop(); // Pop left parenthesis
                                break;
                            default:
                                // Until top operator has greater precedence
                                while (_operators.Count != 0 && _operators.Peek().GreaterThan(@operator))
                                    ApplyTopOperator();
                                _operators.Push(@operator); // Finally, add less prioritized operator
                                break;
                        }
                        break;
                    default:
                        var tokenValue = token.Value;
                        IExpression operand;
                        switch (token.Type)
                        {
                            case TokenType.Numeric:
                                operand = new NumericValue(int.Parse(tokenValue));
                                break;
                            case TokenType.Logical:
                                operand = new LogicalValue(bool.Parse(tokenValue));
                                break;
                            case TokenType.Text:
                                operand = new TextValue(tokenValue);
                                break;
                            default:
                            case TokenType.Variable:
                                if (_operators.Count != 0 && _operators.Peek() == Operator.Operator.StructureInstance)
                                {
                                    operand = _statementParser.ReadInstance(token);
                                }
                                else
                                {
                                    operand = new VariableExpression(
                                        tokenValue,
                                        name => _statementParser._variables.TryGetValue(name, out var v) ? v : null,
                                        (name, variableValue) => _statementParser._variables.Add(name, variableValue));
                                }
                                break;
                        }
                        _operands.Push(operand);
                        break;
                }
            }
            
            while (_operators.Count != 0)
                ApplyTopOperator();

            return _operands.Pop();
        }
        
        private void ApplyTopOperator()
        {
            var @operator = _operators.Pop();

            var left = _operands.Pop();

            if (@operator.SupportsTwoOperands())
            {
                var right = _operands.Pop();
                
                _operands.Push(@operator.ToOperatorExpression(right, left));
            }
            else
            {
                _operands.Push(@operator.ToOperatorExpression(left));
            }
        }
    }
}