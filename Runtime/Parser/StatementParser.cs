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

        // Cached vararg arrays: passing a shared array to a `params` parameter reuses it
        // instead of allocating a fresh one on every call. (TokensStack only reads them.)
        private static readonly TokenType[] StatementLeadTypes =
            { TokenType.Variable, TokenType.This, TokenType.Operator };
        private static readonly string[] IfBranchKeywords = { "elif", "else" };
        private static readonly string[] CommaValue = { "," };
        
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

            // At the top level, anything left over is a construct we cannot
            // start a statement with (stray `end`, dangling `else`, a literal…).
            // Silently stopping here would discard the rest of the script.
            if (parser.Tokens.TryPeek(out var leftover))
                throw new SyntaxException(
                    $"Unexpected `{leftover.Value}` at line {leftover.RowNumber} — not a valid start of a statement");
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
            // Single peek (after skipping empties) + switch, instead of up to four Peek
            // calls that each re-skip empties and allocate params arrays.
            if (!Tokens.TryPeek(out var token))
                return false;
            switch (token.Type)
            {
                case TokenType.Operator:
                case TokenType.Variable:
                case TokenType.This:
                    return true;
                case TokenType.Keyword:
                    // A keyword continues the block unless it closes or branches it.
                    return token.Value != "elif" && token.Value != "else"
                        && token.Value != "rescue" && token.Value != "ensure"
                        && token.Value != "end";
                default:
                    return false;
            }
        }

        private void ParseExpression()
        {
            var token = Tokens.Next(TokenType.Keyword, StatementLeadTypes);
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

            // Reject reassignment to constants (const/enum names)
            if (value is AssignmentOperator assign && assign.Left is VariableExpression ve
                && _script.ConstantNames.Contains(ve.Name))
                throw new SyntaxException($"Cannot reassign constant '{ve.Name}'");

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
                case "const":   ParseConstStatement(token);           break;
                case "enum":    ParseEnumStatement(token);            break;
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
                var type = Tokens.Next(TokenType.Keyword, "if", IfBranchKeywords);
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
                while (Tokens.Peek(TokenType.GroupDivider, ":", CommaValue))
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
            if (ReferenceEquals(loopExpression, _script.NullExpr))
                throw new SyntaxException(
                    $"'loop' at line {rowToken.RowNumber} requires a condition or an iterable");

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

        // ────────────────────────────────────────────────────────
        //  const / enum
        // ────────────────────────────────────────────────────────

        private void ParseConstStatement(Token.Token rowToken)
        {
            var name = Tokens.Next(TokenType.Variable);
            Tokens.Next(TokenType.Operator, "=");
            var valueExpr = ExpressionReader.ReadExpression(_script, Tokens);

            // Register as constant (parser will reject reassignment)
            if (_script.ConstantNames.Contains(name.Value))
                throw new SyntaxException($"Constant '{name.Value}' is already defined");
            _script.ConstantNames.Add(name.Value);

            // Desugar to a normal assignment statement
            var assign = new AssignmentOperator(
                _script,
                new VariableExpression(_script, name.Value),
                valueExpr);
            _compositeStatement.AddStatement(
                new ExpressionStatement(_script, rowToken.RowNumber, _compositeStatement.BlockName, assign));
        }

        /// <summary>
        /// Desugars an enum into a class with numeric properties and a name[] method,
        /// plus a singleton instance assigned to the enum name.
        ///
        /// <code>
        /// enum DamageType
        ///     PHYSICAL
        ///     MAGICAL
        ///     TRUE = 5
        /// end
        /// </code>
        ///
        /// becomes:
        ///
        /// <code>
        /// class DamageType
        ///     PHYSICAL = 0
        ///     MAGICAL = 1
        ///     TRUE = 5
        ///     fun name [value]
        ///         if value == 0 return "PHYSICAL" end
        ///         if value == 1 return "MAGICAL" end
        ///         if value == 5 return "TRUE" end
        ///         return "unknown"
        ///     end
        /// end
        /// DamageType = new DamageType
        /// </code>
        ///
        /// Access: <c>DamageType :: PHYSICAL</c> → 0
        /// Reverse: <c>DamageType :: name [DamageType :: PHYSICAL]</c> → "PHYSICAL"
        /// </summary>
        private void ParseEnumStatement(Token.Token rowToken)
        {
            var enumName = Tokens.Next(TokenType.Variable);
            var line = rowToken.RowNumber;
            var blockName = _compositeStatement.BlockName;

            // Collect members: (name, numericValue)
            var members = new List<(string Name, int Value)>();
            var nextValue = 0;

            while (!Tokens.Peek(TokenType.Keyword, "end"))
            {
                var memberName = Tokens.Next(TokenType.Variable);

                // Optional explicit value: MEMBER = <numeric literal>
                if (Tokens.PeekSameLine(TokenType.Operator, "="))
                {
                    Tokens.Next(); // consume =
                    var explicitExpr = ExpressionReader.ReadExpression(_script, Tokens);

                    if (explicitExpr is ConstantExpression ce && ce.Value.IsNumeric)
                        nextValue = WarValue.ToInt(ce.Value.NumericValue);
                    else
                        throw new SyntaxException(
                            $"Enum value for '{memberName.Value}' must be a numeric literal");
                }

                members.Add((memberName.Value, nextValue));
                nextValue++;
            }
            Tokens.Next(TokenType.Keyword, "end");

            // ── Build the class AST ──

            // Class with no constructor args — properties are set in the body
            var classDetails = new ClassDetails(enumName.Value, new List<string>());
            var classScope = _script.DefinitionContext.NewScope();
            var classStatement = new ClassStatement(_script, line, enumName.Value);
            var classDefinition = new ClassDefinition(classDetails, new List<ClassDetails>(), classStatement, classScope);
            _script.DefinitionContext.GetScope().AddClass(classDefinition);

            // Constructor body: assign each member as a property
            foreach (var (memberName, memberValue) in members)
            {
                var assign = new AssignmentOperator(
                    _script,
                    new VariableExpression(_script, memberName),
                    new ConstantExpression(WarValue.FromNumeric(memberValue)));
                classStatement.AddStatement(
                    new ExpressionStatement(_script, line, enumName.Value, assign));
            }

            // Add `values` property: array of all numeric values
            var valuesElements = new List<IExpression>();
            foreach (var (_, memberValue) in members)
                valuesElements.Add(new ConstantExpression(WarValue.FromNumeric(memberValue)));
            classStatement.AddStatement(
                new ExpressionStatement(_script, line, enumName.Value,
                    new AssignmentOperator(_script,
                        new VariableExpression(_script, "values"),
                        new ArrayExpression(_script, valuesElements))));

            // Add `names` property: array of all string names
            var namesElements = new List<IExpression>();
            foreach (var (memberName, _) in members)
                namesElements.Add(new ConstantExpression(WarValue.FromText(memberName)));
            classStatement.AddStatement(
                new ExpressionStatement(_script, line, enumName.Value,
                    new AssignmentOperator(_script,
                        new VariableExpression(_script, "names"),
                        new ArrayExpression(_script, namesElements))));

            // Add `count` property: number of members
            classStatement.AddStatement(
                new ExpressionStatement(_script, line, enumName.Value,
                    new AssignmentOperator(_script,
                        new VariableExpression(_script, "count"),
                        new ConstantExpression(WarValue.FromNumeric(members.Count)))));

            // Build name[] method: maps numeric value → string name
            var nameMethodBody = new FunctionStatement(_script, line, enumName.Value + "#name");
            var nameMethodScope = _script.DefinitionContext.NewScope();

            // Push scope so the function definition is added to the class scope
            _script.DefinitionContext.PushScope(classScope);
            try
            {
                var nameMethodDetails = new FunctionDetails("name", new List<string> { "value" });
                var nameMethodDef = new FunctionDefinition(nameMethodDetails, nameMethodBody, nameMethodScope);
                classScope.AddFunction(nameMethodDef);
            }
            finally
            {
                _script.DefinitionContext.EndScope();
            }

            // Body of name[]: chain of if value == N return "NAME" end
            foreach (var (memberName, memberValue) in members)
            {
                var condition = new ConditionStatement(_script, line, enumName.Value + "#name");
                var check = new EqualsOperator(
                    _script,
                    new VariableExpression(_script, "value"),
                    new ConstantExpression(WarValue.FromNumeric(memberValue)));

                var thenBody = new CompositeStatement(_script, line, enumName.Value + "#name");
                thenBody.AddStatement(
                    new ReturnStatement(_script, line, enumName.Value + "#name",
                        new ConstantExpression(WarValue.FromText(memberName))));

                condition.AddCase(check, thenBody);
                nameMethodBody.AddStatement(condition);
            }

            // Default return "unknown"
            nameMethodBody.AddStatement(
                new ReturnStatement(_script, line, enumName.Value + "#name",
                    new ConstantExpression(WarValue.FromText("unknown"))));

            // ── Emit: EnumName = new EnumName ──
            var instantiate = new AssignmentOperator(
                _script,
                new VariableExpression(_script, enumName.Value),
                new ClassInstanceOperator(_script,
                    new ClassExpression(_script, enumName.Value, new List<IExpression>())));
            _compositeStatement.AddStatement(
                new ExpressionStatement(_script, line, blockName, instantiate));

            // Protect the enum variable from reassignment
            _script.ConstantNames.Add(enumName.Value);
        }
    }
}