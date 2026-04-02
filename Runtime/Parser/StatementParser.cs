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
        private readonly CompositeStatement _compositeStatement;

        private readonly WarScriptLanguage _script;
        
        private StatementParser(WarScriptLanguage script, TokensStack tokens, CompositeStatement compositeStatement)
        {
            _script = script;
            Tokens = tokens;
            _compositeStatement = compositeStatement;
        }

        private static void Parse(StatementParser parent, CompositeStatement compositeStatement, DefinitionScope definitionScope)
        {
            parent._script.DefinitionContext.PushScope(definitionScope);
            try
            {
                var parser = new StatementParser(parent._script, parent.Tokens, compositeStatement);
                while (parser.HasNextStatement())
                    parser.ParseExpression();
            }
            finally
            {
                parent._script.DefinitionContext.EndScope();
            }
        }

        public static void Parse(WarScriptLanguage script, List<Token.Token> tokens, CompositeStatement compositeStatement)
        {
            var parser = new StatementParser(script, new TokensStack(tokens), compositeStatement);
            while (parser.HasNextStatement())
                parser.ParseExpression();
        }

        /// <summary>
        /// Parse a lambda body from the shared token stream.
        /// Called by ExpressionReader when it encounters <c>fun [params]</c>
        /// in expression position. Parses statements until <c>end</c> and
        /// consumes the <c>end</c> keyword.
        /// </summary>
        internal static void ParseLambdaBody(
            WarScriptLanguage script,
            TokensStack tokens,
            CompositeStatement body,
            DefinitionScope scope)
        {
            script.DefinitionContext.PushScope(scope);
            try
            {
                var parser = new StatementParser(script, tokens, body);
                while (parser.HasNextStatement())
                    parser.ParseExpression();
            }
            finally
            {
                script.DefinitionContext.EndScope();
            }
            tokens.Next(TokenType.Keyword, "end");
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
            var value = ExpressionReader.ReadExpression(_script, Tokens);
            var statement = new ExpressionStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, value);
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
                case "import":  ParseImportStatement(token);          break;
                case "yield":   ParseYieldStatement(token);           break;
                default:
                    throw new SyntaxException($"Failed to parse a keyword: {token.Value}");
            }
        }

        private void ParsePrintStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(_script, Tokens);
            var statement = new PrintStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, expression);
            _compositeStatement.AddStatement(statement);
        }

        private void ParseConditionStatement(Token.Token rowToken)
        {
            Tokens.Back();
            var conditionStatement = new ConditionStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName);

            while (!Tokens.Peek(TokenType.Keyword, "end"))
            {
                // read condition case
                var type = Tokens.Next(TokenType.Keyword, "if", "elif", "else");
                IExpression caseCondition;
                if (type.Value == "else")
                    caseCondition = new ConstantExpression(WarValue.True); // else has no condition
                else
                    caseCondition = ExpressionReader.ReadExpression(_script, Tokens);

                // read case statements
                var caseStatement = new CompositeStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName);
                var caseScope = _script.DefinitionContext.NewScope();
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
            var baseTypes = new List<ClassDetails>();
            if (Tokens.Peek(TokenType.GroupDivider, ":"))
            {
                while (Tokens.Peek(TokenType.GroupDivider, ":", ","))
                {
                    Tokens.Next();
                    baseTypes.Add(ReadClassDetails());
                }
            }

            // add class definition
            var classScope = _script.DefinitionContext.NewScope();
            var classStatement = new ClassStatement(_script, rowToken.RowNumber, classDetails.Name);
            var classDefinition = new ClassDefinition(classDetails, baseTypes, classStatement, classScope);
            _script.DefinitionContext.GetScope().AddClass(classDefinition);

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
            var defaults = new List<(string Name, IExpression DefaultExpr)>();
            var minArity = -1;

            if (Tokens.Peek(TokenType.GroupDivider, "["))
            {
                Tokens.Next(TokenType.GroupDivider, "["); // skip open square bracket

                while (!Tokens.Peek(TokenType.GroupDivider, "]"))
                {
                    var argumentToken = Tokens.Next(TokenType.Variable);
                    arguments.Add(argumentToken.Value);

                    // Check for default value: param = expr
                    if (Tokens.PeekSameLine(TokenType.Operator, "="))
                    {
                        Tokens.Next(); // consume =
                        if (minArity < 0) minArity = arguments.Count - 1;
                        var defaultExpr = ExpressionReader.ReadExpression(_script, Tokens);
                        defaults.Add((argumentToken.Value, defaultExpr));
                    }
                    else if (minArity >= 0)
                    {
                        throw new SyntaxException(
                            $"Required parameter '{argumentToken.Value}' cannot follow a parameter with a default value");
                    }

                    if (Tokens.Peek(TokenType.GroupDivider, ","))
                        Tokens.Next();
                }

                Tokens.Next(TokenType.GroupDivider, "]"); // skip close square bracket
            }

            // build block name — prefix with class name if inside a class
            var blockName = _compositeStatement is ClassStatement
                ? _compositeStatement.BlockName + "#" + name.Value
                : name.Value;

            var functionStatement = new FunctionStatement(_script, rowToken.RowNumber, blockName);
            var functionScope = _script.DefinitionContext.NewScope();
            var functionDetails = new FunctionDetails(name.Value, arguments, minArity);
            var functionDefinition = new FunctionDefinition(functionDetails, functionStatement, functionScope);
            _script.DefinitionContext.GetScope().AddFunction(functionDefinition);

            // parse function statements
            Parse(this, functionStatement, functionScope);
            Tokens.Next(TokenType.Keyword, "end");

            // Desugar default parameter values.
            // For each defaulted param, inject at the top of the body:
            //     if <param> == null
            //         <param> = <default_expr>
            //     end
            // Inserted in reverse order so they appear in parameter order.
            for (var i = defaults.Count - 1; i >= 0; i--)
            {
                var (paramName, defaultExpr) = defaults[i];
                var line = rowToken.RowNumber;

                var nullCheck = new EqualsOperator(
                    _script,
                    new VariableExpression(_script, paramName),
                    _script.NullExpr);

                var assignBody = new CompositeStatement(_script, line, blockName);
                var assign = new AssignmentOperator(
                    _script,
                    new VariableExpression(_script, paramName),
                    defaultExpr);
                assignBody.AddStatement(new ExpressionStatement(_script, line, blockName, assign));

                var condition = new ConditionStatement(_script, line, blockName);
                condition.AddCase(nullCheck, assignBody);

                functionStatement.StatementsToExecute.Insert(0, condition);
            }
        }

        private void ParseReturnStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(_script, Tokens);
            var statement = new ReturnStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, expression);
            _compositeStatement.AddStatement(statement);
        }

        private void ParseLoopStatement(Token.Token rowToken)
        {
            var loopExpression = ExpressionReader.ReadExpression(_script, Tokens);
            if (!(loopExpression is IOperatorExpression || loopExpression is VariableExpression))
                return;

            AbstractLoopStatement loopStatement;

            if (loopExpression is VariableExpression variable && Tokens.Peek(TokenType.Keyword, "in"))
            {
                // loop <variable> in <bounds>
                Tokens.Next(TokenType.Keyword, "in");
                var bounds = ExpressionReader.ReadExpression(_script, Tokens);

                if (Tokens.Peek(TokenType.GroupDivider, ".."))
                {
                    Tokens.Next(TokenType.GroupDivider, "..");
                    var upperBound = ExpressionReader.ReadExpression(_script, Tokens);

                    if (Tokens.Peek(TokenType.Keyword, "by"))
                    {
                        // loop <variable> in <lower_bound>..<upper_bound> by <step>
                        Tokens.Next(TokenType.Keyword, "by");
                        var step = ExpressionReader.ReadExpression(_script, Tokens);
                        loopStatement = new ForLoopStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds, upperBound, step);
                    }
                    else
                    {
                        // loop <variable> in <lower_bound>..<upper_bound>
                        loopStatement = new ForLoopStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds, upperBound);
                    }
                }
                else
                {
                    // loop <variable> in <iterable>
                    loopStatement = new IterableLoopStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, variable, bounds);
                }
            }
            else
            {
                // loop <condition>
                loopStatement = new WhileLoopStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, loopExpression);
            }

            var loopScope = _script.DefinitionContext.NewScope();
            Parse(this, loopStatement, loopScope);
            Tokens.Next(TokenType.Keyword, "end");

            _compositeStatement.AddStatement(loopStatement);
        }

        private void ParseBreakStatement(Token.Token rowToken)
        {
            _compositeStatement.AddStatement(new BreakStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName));
        }

        private void ParseNextStatement(Token.Token rowToken)
        {
            _compositeStatement.AddStatement(new NextStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName));
        }

        private void ParseAssertStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(_script, Tokens);
            _compositeStatement.AddStatement(new AssertStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, expression));
        }

        private void ParseRaiseExceptionStatement(Token.Token rowToken)
        {
            var expression = ExpressionReader.ReadExpression(_script, Tokens);
            _compositeStatement.AddStatement(new RaiseExceptionStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, expression));
        }

        private void ParseHandleExceptionStatement(Token.Token rowToken)
        {
            // read begin block
            var beginStatement = new CompositeStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName);
            Parse(this, beginStatement, _script.DefinitionContext.NewScope());

            // read rescue block
            CompositeStatement rescueStatement = null;
            string errorVariable = null;
            if (Tokens.Peek(TokenType.Keyword, "rescue"))
            {
                Tokens.Next();

                if (Tokens.PeekSameLine(TokenType.Variable))
                    errorVariable = Tokens.Next().Value;

                rescueStatement = new CompositeStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName);
                Parse(this, rescueStatement, _script.DefinitionContext.NewScope());
            }

            // read ensure block
            CompositeStatement ensureStatement = null;
            if (Tokens.Peek(TokenType.Keyword, "ensure"))
            {
                Tokens.Next();
                ensureStatement = new CompositeStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName);
                Parse(this, ensureStatement, _script.DefinitionContext.NewScope());
            }

            Tokens.Next(TokenType.Keyword, "end");

            _compositeStatement.AddStatement(new HandleExceptionStatement(_script, 
                rowToken.RowNumber, _compositeStatement.BlockName,
                beginStatement, rescueStatement, ensureStatement, errorVariable));
        }

        private void ParseImportStatement(Token.Token rowToken)
        {
            var pathToken = Tokens.Next(TokenType.Text);
            var statement = new ImportStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, pathToken.Value);
            _compositeStatement.AddStatement(statement);
        }
        
        private void ParseYieldStatement(Token.Token rowToken)
        {
            YieldType yieldType;
            IExpression expression = null;

            // Check for "wait" or "until" as contextual words (they remain valid variable names elsewhere)
            if (Tokens.PeekSameLine(TokenType.Variable, "wait"))
            {
                Tokens.Next(); // consume "wait"
                yieldType = YieldType.Wait;
                expression = ExpressionReader.ReadExpression(_script, Tokens);
            }
            else if (Tokens.PeekSameLine(TokenType.Variable, "until"))
            {
                Tokens.Next(); // consume "until"
                yieldType = YieldType.Until;
                expression = ExpressionReader.ReadExpression(_script, Tokens);
            }
            else
            {
                yieldType = YieldType.NextTick;
            }

            _compositeStatement.AddStatement(
                new YieldStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName,
                    yieldType, expression));
        }
    }
}