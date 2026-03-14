using System;
using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;
using WarScript.Statement;
using WarScript.Statement.Loop;
using WarScript.Token;

namespace WarScript
{
    public class StatementParser
    {
        public TokensStack Tokens { get; }
        private readonly Func<string> _readLine;
        private readonly CompositeStatement _compositeStatement;

        public StatementParser(TokensStack tokens, Func<string> readLine, CompositeStatement compositeStatement)
        {
            Tokens = tokens;
            _readLine = readLine;
            _compositeStatement = compositeStatement;
        }

        public static void Parse(StatementParser parent, CompositeStatement compositeStatement, DefinitionScope definitionScope)
        {
            DefinitionContext.PushScope(definitionScope);
            try
            {
                var parser = new StatementParser(parent.Tokens, parent._readLine, compositeStatement);
                while (parser.HasNextStatement())
                    parser.ParseExpression();
            }
            finally
            {
                DefinitionContext.EndScope();
            }
        }

        public static void Parse(List<Token.Token> tokens, CompositeStatement compositeStatement)
        {
            var parser = new StatementParser(new TokensStack(tokens), Console.ReadLine, compositeStatement);
            while (parser.HasNextStatement())
                parser.ParseExpression();
        }

        private bool HasNextStatement()
        {
            if (!Tokens.HasNext())
                return false;
            if (Tokens.Peek(TokenType.Operator, TokenType.Variable, TokenType.This))
                return true;
            if (Tokens.Peek(TokenType.Keyword))
                return !Tokens.Peek(TokenType.Keyword, "elif", "else", "rescue", "ensure", "end");
            return false;
        }

        private void ParseExpression()
        {
            var token = Tokens.Next(TokenType.Keyword, TokenType.Variable, TokenType.This, TokenType.Operator);
            switch (token.Type)
            {
                case TokenType.Variable:
                case TokenType.Operator:
                case TokenType.This:
                    ParseExpressionStatement(token);
                    break;
                case TokenType.Keyword:
                    ParseKeywordStatement(token);
                    break;
                default:
                    throw new SyntaxException($"Statement can't start with the following lexeme `{token}`");
            }
        }

        private void ParseExpressionStatement(Token.Token rowToken)
        {
            Tokens.Back();
            var value = ExpressionReader.ReadExpression(Tokens);
            var statement = new ExpressionStatement(rowToken.RowNumber, _compositeStatement.BlockName, value);
            _compositeStatement.AddStatement(statement);
        }

        private void ParseKeywordStatement(Token.Token token)
        {
            switch (token.Value)
            {
                case "print":   ParsePrintStatement(token);           break;
                case "if":      ParseConditionStatement(token);       break;
                case "class":   ParseClassDefinition(token);          break;
                case "fun":     ParseFunctionDefinition(token);       break;
                case "return":  ParseReturnStatement(token);          break;
                case "loop":    ParseLoopStatement(token);            break;
                case "break":   ParseBreakStatement(token);           break;
                case "next":    ParseNextStatement(token);            break;
                case "assert":  ParseAssertStatement(token);          break;
                case "raise":   ParseRaiseExceptionStatement(token);  break;
                case "begin":   ParseHandleExceptionStatement(token); break;
                default:
                    throw new SyntaxException($"Failed to parse a keyword: {token.Value}");
            }
        }

        private void ParsePrintStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(Tokens);
            var statement = new PrintStatement(rowToken.RowNumber, _compositeStatement.BlockName, expression);
            _compositeStatement.AddStatement(statement);
        }

        private void ParseConditionStatement(Token.Token rowToken)
        {
            Tokens.Back();
            var conditionStatement = new ConditionStatement(rowToken.RowNumber, _compositeStatement.BlockName);

            while (!Tokens.Peek(TokenType.Keyword, "end"))
            {
                // read condition case
                var type = Tokens.Next(TokenType.Keyword, "if", "elif", "else");
                IExpression caseCondition;
                if (type.Value == "else")
                    caseCondition = new LogicalValue(true); // else has no condition
                else
                    caseCondition = ExpressionReader.ReadExpression(Tokens);

                // read case statements
                var caseStatement = new CompositeStatement(rowToken.RowNumber, _compositeStatement.BlockName);
                var caseScope = DefinitionContext.NewScope();
                Parse(this, caseStatement, caseScope);

                conditionStatement.AddCase(caseCondition, caseStatement);
            }
            Tokens.Next(TokenType.Keyword, "end");

            _compositeStatement.AddStatement(conditionStatement);
        }

        private void ParseClassDefinition(Token.Token rowToken)
        {
            // read class details
            var classDetails = ReadClassDetails();

            // read base types
            var baseTypes = new HashSet<ClassDetails>();
            if (Tokens.Peek(TokenType.GroupDivider, ":"))
            {
                while (Tokens.Peek(TokenType.GroupDivider, ":", ","))
                {
                    Tokens.Next();
                    baseTypes.Add(ReadClassDetails());
                }
            }

            // add class definition
            var classScope = DefinitionContext.NewScope();
            var classStatement = new ClassStatement(rowToken.RowNumber, classDetails.Name);
            var classDefinition = new ClassDefinition(classDetails, baseTypes, classStatement, classScope);
            DefinitionContext.GetScope().AddClass(classDefinition);

            // parse class's statements
            Parse(this, classStatement, classScope);
            Tokens.Next(TokenType.Keyword, "end");
        }

        private ClassDetails ReadClassDetails()
        {
            var className = Tokens.Next(TokenType.Variable);
            var classArguments = new List<string>();

            if (Tokens.Peek(TokenType.GroupDivider, "["))
            {
                Tokens.Next(); // skip open square bracket

                while (!Tokens.Peek(TokenType.GroupDivider, "]"))
                {
                    var argumentToken = Tokens.Next(TokenType.Variable);
                    classArguments.Add(argumentToken.Value);

                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }

                Tokens.Next(TokenType.GroupDivider, "]"); // skip close square bracket
            }

            return new ClassDetails(className.Value, classArguments);
        }

        private void ParseFunctionDefinition(Token.Token rowToken)
        {
            var name = Tokens.Next(TokenType.Variable);
            var arguments = new List<string>();

            if (Tokens.Peek(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "["); // skip open square bracket

                while (!Tokens.Peek(TokenType.GroupDivider, "]"))
                {
                    var argumentToken = Tokens.Next(TokenType.Variable);
                    arguments.Add(argumentToken.Value);

                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }

                Tokens.Next(TokenType.GroupDivider, "]"); // skip close square bracket
            }

            // build block name — prefix with class name if inside a class
            var blockName = _compositeStatement is ClassStatement
                ? _compositeStatement.BlockName + "#" + name.Value
                : name.Value;

            var functionStatement = new FunctionStatement(rowToken.RowNumber, blockName);
            var functionScope = DefinitionContext.NewScope();
            var functionDetails = new FunctionDetails(name.Value, arguments);
            var functionDefinition = new FunctionDefinition(functionDetails, functionStatement, functionScope);
            DefinitionContext.GetScope().AddFunction(functionDefinition);

            // parse function statements
            Parse(this, functionStatement, functionScope);
            Tokens.Next(TokenType.Keyword, "end");
        }

        private void ParseReturnStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(Tokens);
            var statement = new ReturnStatement(rowToken.RowNumber, _compositeStatement.BlockName, expression);
            _compositeStatement.AddStatement(statement);
        }

        private void ParseLoopStatement(Token.Token rowToken)
        {
            var loopExpression = ExpressionReader.ReadExpression(Tokens);
            if (!(loopExpression is IOperatorExpression || loopExpression is VariableExpression))
                return;

            AbstractLoopStatement loopStatement;

            if (loopExpression is VariableExpression variable && Tokens.Peek(TokenType.Keyword, "in"))
            {
                // loop <variable> in <bounds>
                Tokens.Next(TokenType.Keyword, "in");
                var bounds = ExpressionReader.ReadExpression(Tokens);

                if (Tokens.Peek(TokenType.GroupDivider, ".."))
                {
                    Tokens.Next(TokenType.GroupDivider, "..");
                    var upperBound = ExpressionReader.ReadExpression(Tokens);

                    if (Tokens.Peek(TokenType.Keyword, "by"))
                    {
                        // loop <variable> in <lower_bound>..<upper_bound> by <step>
                        Tokens.Next(TokenType.Keyword, "by");
                        var step = ExpressionReader.ReadExpression(Tokens);
                        loopStatement = new ForLoopStatement(rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds, upperBound, step);
                    }
                    else
                    {
                        // loop <variable> in <lower_bound>..<upper_bound>
                        loopStatement = new ForLoopStatement(rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds, upperBound);
                    }
                }
                else
                {
                    // loop <variable> in <iterable>
                    loopStatement = new IterableLoopStatement(rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds);
                }
            }
            else
            {
                // loop <condition>
                loopStatement = new WhileLoopStatement(rowToken.RowNumber, _compositeStatement.BlockName, loopExpression);
            }

            var loopScope = DefinitionContext.NewScope();
            Parse(this, loopStatement, loopScope);
            Tokens.Next(TokenType.Keyword, "end");

            _compositeStatement.AddStatement(loopStatement);
        }

        private void ParseBreakStatement(Token.Token rowToken)
        {
            _compositeStatement.AddStatement(new BreakStatement(rowToken.RowNumber, _compositeStatement.BlockName));
        }

        private void ParseNextStatement(Token.Token rowToken)
        {
            _compositeStatement.AddStatement(new NextStatement(rowToken.RowNumber, _compositeStatement.BlockName));
        }

        private void ParseAssertStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(Tokens);
            _compositeStatement.AddStatement(new AssertStatement(rowToken.RowNumber, _compositeStatement.BlockName, expression));
        }

        private void ParseRaiseExceptionStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(Tokens);
            _compositeStatement.AddStatement(new RaiseExceptionStatement(rowToken.RowNumber, _compositeStatement.BlockName, expression));
        }

        private void ParseHandleExceptionStatement(Token.Token rowToken)
        {
            // read begin block
            var beginStatement = new CompositeStatement(rowToken.RowNumber, _compositeStatement.BlockName);
            Parse(this, beginStatement, DefinitionContext.NewScope());

            // read rescue block
            CompositeStatement rescueStatement = null;
            string errorVariable = null;
            if (Tokens.Peek(TokenType.Keyword, "rescue"))
            {
                Tokens.Next();

                if (Tokens.PeekSameLine(TokenType.Variable))
                    errorVariable = Tokens.Next().Value;

                rescueStatement = new CompositeStatement(rowToken.RowNumber, _compositeStatement.BlockName);
                Parse(this, rescueStatement, DefinitionContext.NewScope());
            }

            // read ensure block
            CompositeStatement ensureStatement = null;
            if (Tokens.Peek(TokenType.Keyword, "ensure"))
            {
                Tokens.Next();
                ensureStatement = new CompositeStatement(rowToken.RowNumber, _compositeStatement.BlockName);
                Parse(this, ensureStatement, DefinitionContext.NewScope());
            }

            Tokens.Next(TokenType.Keyword, "end");

            _compositeStatement.AddStatement(new HandleExceptionStatement(
                rowToken.RowNumber, _compositeStatement.BlockName,
                beginStatement, rescueStatement, ensureStatement, errorVariable));
        }
    }
}