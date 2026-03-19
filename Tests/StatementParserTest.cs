using System.Collections.Generic;
using NUnit.Framework;
using WarScript.Context.Definition;
using WarScript.Expression.Value;
using WarScript.Tests;

namespace WarScript
{
    public class StatementParserTest
    {
        [Test]
        public void Print_Numeric_Expression()
        {
            var (_, output) = TestHelper.Run("print 2 + 3");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Variable_Assignment_And_Retrieval()
        {
            var (_, output) = TestHelper.Run(@"
                x = 10
                print x
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void Function_Definition_And_Call()
        {
            var (_, output) = TestHelper.Run(@"
                fun add [a, b]
                    return a + b
                end
                print add [3, 4]
            ");
            Assert.AreEqual(new[] { "7" }, output);
        }

        [Test]
        public void For_Loop_Accumulates()
        {
            var (_, output) = TestHelper.Run(@"
                sum = 0
                loop i in 0..5
                    sum = sum + i
                end
                print sum
            ");
            Assert.AreEqual(new[] { "10" }, output);
        }

        [Test]
        public void Class_Properties_And_Methods()
        {
            var (_, output) = TestHelper.Run(@"
                class Vec2 [x, y]
                    fun length []
                        return (x ** 2 + y ** 2) ** 0.5
                    end
                end
                v = new Vec2 [3, 4]
                print v :: length []
            ");
            Assert.AreEqual(new[] { "5" }, output);
        }

        [Test]
        public void Native_Function_Binding()
        {
            var output = new List<string>();
            var script = new WarScriptLanguage(
                "test",
                "print double [21]",
                scope =>
                {
                    scope.AddFunction(new NativeFunctionDefinition(
                        new FunctionDetails("double", new List<string> { "n" }),
                        args =>
                        {
                            var n = NativeHelper.Arg<NumericValue>(args, 0);
                            return new NumericValue(null, n.GetValue() * 2);
                        },
                        "Doubles a number", "NumericValue"));
                },
                null,
                (s, msg) => output.Add(msg));

            Assert.AreEqual(new[] { "42" }, output);
        }

        [Test]
        public void Exception_Is_Caught_In_Rescue()
        {
            var (_, output) = TestHelper.Run(@"
                begin
                    raise ""boom""
                    print ""unreachable""
                rescue e
                    print e
                end
            ");
            Assert.AreEqual(new[] { "boom" }, output);
        }

        [Test]
        public void Short_Circuit_And_Skips_Right()
        {
            var (_, output) = TestHelper.Run(@"
                x = 0
                if false and (x = 1) == 1
                    print ""branch taken""
                end
                print x
            ");
            // x should remain 0: the assignment should never execute
            Assert.AreEqual(new[] { "0" }, output);
        }
        
        /*
        [Fact]
        public void PrintTest()
        {
            var tokens = new List<Token>
            {
                new Token { Type = TokenType.Keyword, Value = "print" },
                new Token { Type = TokenType.Text, Value = "Hello World" }
            };
            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            var statement = new CompositeStatement(null, "printTest");
            StatementParser.Parse(tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.Single(statements);

            Assert.IsType<PrintStatement>(statements[0]);
            var printStatement = (PrintStatement)statements[0];

            Assert.IsType<TextValue>(printStatement.Expression);
            var textValue = (TextValue)printStatement.Expression;

            Assert.Equal("Hello World", textValue.GetValue());

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        [Fact]
        public void TestAssignment()
        {
            var tokens = new List<Token>
            {
                new Token { Type = TokenType.Variable, Value = "a" },
                new Token { Type = TokenType.Operator, Value = "=" },
                new Token { Type = TokenType.Numeric, Value = "2" },
                new Token { Type = TokenType.Operator, Value = "+" },
                new Token { Type = TokenType.Numeric, Value = "5" }
            };
            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            var statement = new CompositeStatement(null, "testAssignment");
            StatementParser.Parse(tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.Single(statements);

            Assert.IsType<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsType<AssignmentOperator>(expressionStatement.Expression);
            var assignOperator = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsType<VariableExpression>(assignOperator.Left);
            var variableExpression = (VariableExpression)assignOperator.Left;
            Assert.Equal("a", variableExpression.Name);

            Assert.IsType<AdditionOperator>(assignOperator.Right);
            var addOperator = (AdditionOperator)assignOperator.Right;

            Assert.IsType<NumericValue>(addOperator.Left);
            var left = (NumericValue)addOperator.Left;
            Assert.Equal(2, left.GetValue());

            Assert.IsType<NumericValue>(addOperator.Right);
            var right = (NumericValue)addOperator.Right;
            Assert.Equal(5, right.GetValue());

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        [Fact]
        public void TestCondition()
        {
            var tokens = new List<Token>
            {
                new Token { Type = TokenType.Keyword, Value = "if" },
                new Token { Type = TokenType.Variable, Value = "a" },
                new Token { Type = TokenType.Operator, Value = ">" },
                new Token { Type = TokenType.Numeric, Value = "5" },
                new Token { Type = TokenType.Keyword, Value = "print" },
                new Token { Type = TokenType.Text, Value = "a is greater than 5" },
                new Token { Type = TokenType.Keyword, Value = "elif" },
                new Token { Type = TokenType.Variable, Value = "a" },
                new Token { Type = TokenType.Operator, Value = ">=" },
                new Token { Type = TokenType.Numeric, Value = "1" },
                new Token { Type = TokenType.Keyword, Value = "print" },
                new Token { Type = TokenType.Text, Value = "a is greater than or equal to 1" },
                new Token { Type = TokenType.Keyword, Value = "else" },
                new Token { Type = TokenType.Keyword, Value = "print" },
                new Token { Type = TokenType.Text, Value = "a is less than 1" },
                new Token { Type = TokenType.Keyword, Value = "end" }
            };
            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            var statement = new CompositeStatement(null, "testCondition");
            StatementParser.Parse(tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.Single(statements);

            Assert.IsType<ConditionStatement>(statements[0]);
            var conditionStatement = (ConditionStatement)statements[0];

            var cases = conditionStatement.Cases;
            Assert.Equal(3, cases.Count);

            var conditions = cases.Keys.ToList();

            // if case
            Assert.IsType<GreaterThanOperator>(conditions[0]);
            var ifCondition = (GreaterThanOperator)conditions[0];

            Assert.IsType<VariableExpression>(ifCondition.Left);
            var ifLeftExpression = (VariableExpression)ifCondition.Left;
            Assert.Equal("a", ifLeftExpression.Name);

            var ifRightExpression = (NumericValue)ifCondition.Right;
            Assert.Equal(5, ifRightExpression.GetValue());

            var ifStatements = cases[ifCondition].StatementsToExecute;
            Assert.Single(ifStatements);

            Assert.IsType<PrintStatement>(ifStatements[0]);
            var ifPrintStatement = (PrintStatement)ifStatements[0];

            Assert.IsType<TextValue>(ifPrintStatement.Expression);
            var ifPrintValue = (TextValue)ifPrintStatement.Expression;
            Assert.Equal("a is greater than 5", ifPrintValue.GetValue());

            // elif case
            Assert.IsType<GreaterThanOrEqualToOperator>(conditions[1]);
            var elifCondition = (GreaterThanOrEqualToOperator)conditions[1];

            Assert.IsType<VariableExpression>(elifCondition.Left);
            var elifLeftExpression = (VariableExpression)elifCondition.Left;
            Assert.Equal("a", elifLeftExpression.Name);

            var elifRightExpression = (NumericValue)elifCondition.Right;
            Assert.Equal(1, elifRightExpression.GetValue());

            var elifStatements = cases[elifCondition].StatementsToExecute;
            Assert.Single(elifStatements);

            Assert.IsType<PrintStatement>(elifStatements[0]);
            var elifPrintStatement = (PrintStatement)elifStatements[0];

            Assert.IsType<TextValue>(elifPrintStatement.Expression);
            var elifPrintValue = (TextValue)elifPrintStatement.Expression;
            Assert.Equal("a is greater than or equal to 1", elifPrintValue.GetValue());

            // else case
            Assert.IsType<LogicalValue>(conditions[2]);
            var elseCondition = (LogicalValue)conditions[2];

            Assert.True(elseCondition.GetValue());

            var elseStatements = cases[elseCondition].StatementsToExecute;
            Assert.Single(elseStatements);

            Assert.IsType<PrintStatement>(elseStatements[0]);
            var elsePrintStatement = (PrintStatement)elseStatements[0];

            Assert.IsType<TextValue>(elsePrintStatement.Expression);
            var elsePrintValue = (TextValue)elsePrintStatement.Expression;
            Assert.Equal("a is less than 1", elsePrintValue.GetValue());

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        [Fact]
        public void TestClass()
        {
            var tokens = new List<Token>
            {
                new Token { Type = TokenType.Keyword, Value = "class", RowNumber = 1 },
                new Token { Type = TokenType.Variable, Value = "Person", RowNumber = 1 },
                new Token { Type = TokenType.GroupDivider, Value = "[", RowNumber = 1 },
                new Token { Type = TokenType.Variable, Value = "name", RowNumber = 1 },
                new Token { Type = TokenType.GroupDivider, Value = ",", RowNumber = 1 },
                new Token { Type = TokenType.Variable, Value = "age", RowNumber = 1 },
                new Token { Type = TokenType.GroupDivider, Value = "]", RowNumber = 1 },
                new Token { Type = TokenType.LineBreak, Value = "\n", RowNumber = 1 },
                new Token { Type = TokenType.Keyword, Value = "end", RowNumber = 2 },
                new Token { Type = TokenType.LineBreak, Value = "\n", RowNumber = 2 },
                new Token { Type = TokenType.Variable, Value = "person", RowNumber = 3 },
                new Token { Type = TokenType.Operator, Value = "=", RowNumber = 3 },
                new Token { Type = TokenType.Operator, Value = "new", RowNumber = 3 },
                new Token { Type = TokenType.Variable, Value = "Person", RowNumber = 3 },
                new Token { Type = TokenType.GroupDivider, Value = "[", RowNumber = 3 },
                new Token { Type = TokenType.Text, Value = "Randy Marsh", RowNumber = 3 },
                new Token { Type = TokenType.GroupDivider, Value = ",", RowNumber = 3 },
                new Token { Type = TokenType.Numeric, Value = "45", RowNumber = 3 },
                new Token { Type = TokenType.GroupDivider, Value = "]", RowNumber = 3 },
                new Token { Type = TokenType.LineBreak, Value = "\n", RowNumber = 3 },
                new Token { Type = TokenType.Keyword, Value = "print", RowNumber = 4 },
                new Token { Type = TokenType.Variable, Value = "person", RowNumber = 4 },
                new Token { Type = TokenType.Operator, Value = "::", RowNumber = 4 },
                new Token { Type = TokenType.Variable, Value = "name", RowNumber = 4 },
                new Token { Type = TokenType.Operator, Value = "+", RowNumber = 4 },
                new Token { Type = TokenType.Text, Value = " is ", RowNumber = 4 },
                new Token { Type = TokenType.Operator, Value = "+", RowNumber = 4 },
                new Token { Type = TokenType.Variable, Value = "person", RowNumber = 4 },
                new Token { Type = TokenType.Operator, Value = "::", RowNumber = 4 },
                new Token { Type = TokenType.Variable, Value = "age", RowNumber = 4 },
                new Token { Type = TokenType.Operator, Value = "+", RowNumber = 4 },
                new Token { Type = TokenType.Text, Value = " years old", RowNumber = 4 }
            };
            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            var statement = new CompositeStatement(null, "testClass");
            StatementParser.Parse(tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.Equal(2, statements.Count);

            // 1st statement
            Assert.IsType<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsType<AssignmentOperator>(expressionStatement.Expression);
            var assignStatement = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsType<VariableExpression>(assignStatement.Left);
            var variableExpression = (VariableExpression)assignStatement.Left;
            Assert.Equal("person", variableExpression.Name);

            Assert.IsType<ClassInstanceOperator>(assignStatement.Right);
            var instanceOperator = (ClassInstanceOperator)assignStatement.Right;

            Assert.IsType<ClassExpression>(instanceOperator.Value);
            var type = (ClassExpression)instanceOperator.Value;

            Assert.Equal("Person", type.Name);
            Assert.Equal("Randy Marsh", type.PropertiesExpressions[0].ToString());
            Assert.Equal("45", type.PropertiesExpressions[1].ToString());

            // 2nd statement
            var printStatement = (PrintStatement)statements[1];
            Assert.IsType<AdditionOperator>(printStatement.Expression);

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        [Fact]
        public void TestComment()
        {
            var tokens = new List<Token>
            {
                new Token { Type = TokenType.Comment, Value = "# a = 5" },
                new Token { Type = TokenType.LineBreak, Value = "\n" },
                new Token { Type = TokenType.Variable, Value = "a" },
                new Token { Type = TokenType.Operator, Value = "=" },
                new Token { Type = TokenType.Numeric, Value = "5" },
                new Token { Type = TokenType.Comment, Value = "# a is equal to 5" }
            };
            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            var statement = new CompositeStatement(null, "testComment");
            StatementParser.Parse(tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.Single(statements);

            Assert.IsType<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsType<AssignmentOperator>(expressionStatement.Expression);
            var assignStatement = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsType<VariableExpression>(assignStatement.Left);
            var variableExpression = (VariableExpression)assignStatement.Left;
            Assert.Equal("a", variableExpression.Name);

            Assert.IsType<NumericValue>(assignStatement.Right);
            var numericValue = (NumericValue)assignStatement.Right;

            Assert.Equal(5, numericValue.GetValue());

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }
        */
    }
}
