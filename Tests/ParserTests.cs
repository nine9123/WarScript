using System.Collections.Generic;
using NUnit.Framework;
using WarScript;
using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;
using WarScript.Statement;
using WarScript.Token;

namespace Tests
{
    [TestFixture]
    public class ParserTests
    {
        private WarScriptLanguage _script;

        [SetUp]
        public void SetUp()
        {
            // Create a minimal script instance for context access.
            // Empty source means the constructor lexes/parses/executes nothing.
            // After construction the context stacks are empty, so we push our own scopes.
            _script = new WarScriptLanguage("test", "", _ => { }, null, null);
            _script.DefinitionContext.PushScope(_script.DefinitionContext.NewScope());
            _script.MemoryContext.PushScope(_script.MemoryContext.NewScope());
        }

        [TearDown]
        public void TearDown()
        {
            _script.DefinitionContext.EndScope();
            _script.MemoryContext.EndScope();
        }

        private WarScript.Token.Token T(TokenType type, string value, int row = 1)
        {
            return new WarScript.Token.Token(type, value, row);
        }

        [Test]
        public void PrintTest()
        {
            var tokens = new List<WarScript.Token.Token>
            {
                T(TokenType.Keyword, "print"),
                T(TokenType.Text, "Hello World")
            };
            var statement = new CompositeStatement(_script, null, "printTest");
            StatementParser.Parse(_script, tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.AreEqual(1, statements.Count);

            Assert.IsInstanceOf<PrintStatement>(statements[0]);
            var printStatement = (PrintStatement)statements[0];

            Assert.IsInstanceOf<TextValue>(printStatement.Expression);
            var textValue = (TextValue)printStatement.Expression;

            Assert.AreEqual("Hello World", textValue.GetValue());
        }

        [Test]
        public void TestAssignment()
        {
            var tokens = new List<WarScript.Token.Token>
            {
                T(TokenType.Variable, "a"),
                T(TokenType.Operator, "="),
                T(TokenType.Numeric, "2"),
                T(TokenType.Operator, "+"),
                T(TokenType.Numeric, "5")
            };
            var statement = new CompositeStatement(_script, null, "testAssignment");
            StatementParser.Parse(_script, tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.AreEqual(1, statements.Count);

            Assert.IsInstanceOf<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsInstanceOf<AssignmentOperator>(expressionStatement.Expression);
            var assignOperator = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsInstanceOf<VariableExpression>(assignOperator.Left);
            var variableExpression = (VariableExpression)assignOperator.Left;
            Assert.AreEqual("a", variableExpression.Name);

            Assert.IsInstanceOf<AdditionOperator>(assignOperator.Right);
            var addOperator = (AdditionOperator)assignOperator.Right;

            Assert.IsInstanceOf<NumericValue>(addOperator.Left);
            var left = (NumericValue)addOperator.Left;
            Assert.AreEqual(2, left.GetValue());

            Assert.IsInstanceOf<NumericValue>(addOperator.Right);
            var right = (NumericValue)addOperator.Right;
            Assert.AreEqual(5, right.GetValue());
        }

        [Test]
        public void TestCondition()
        {
            var tokens = new List<WarScript.Token.Token>
            {
                T(TokenType.Keyword, "if"),
                T(TokenType.Variable, "a"),
                T(TokenType.Operator, ">"),
                T(TokenType.Numeric, "5"),
                T(TokenType.Keyword, "print"),
                T(TokenType.Text, "a is greater than 5"),
                T(TokenType.Keyword, "elif"),
                T(TokenType.Variable, "a"),
                T(TokenType.Operator, ">="),
                T(TokenType.Numeric, "1"),
                T(TokenType.Keyword, "print"),
                T(TokenType.Text, "a is greater than or equal to 1"),
                T(TokenType.Keyword, "else"),
                T(TokenType.Keyword, "print"),
                T(TokenType.Text, "a is less than 1"),
                T(TokenType.Keyword, "end")
            };
            var statement = new CompositeStatement(_script, null, "testCondition");
            StatementParser.Parse(_script, tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.AreEqual(1, statements.Count);

            Assert.IsInstanceOf<ConditionStatement>(statements[0]);
            var conditionStatement = (ConditionStatement)statements[0];

            var cases = conditionStatement.Cases;
            Assert.AreEqual(3, cases.Count);

            // if case
            var ifCondition = cases[0].Key;
            var ifBody = cases[0].Value;

            Assert.IsInstanceOf<GreaterThanOperator>(ifCondition);
            var gtOp = (GreaterThanOperator)ifCondition;

            Assert.IsInstanceOf<VariableExpression>(gtOp.Left);
            Assert.AreEqual("a", ((VariableExpression)gtOp.Left).Name);

            Assert.IsInstanceOf<NumericValue>(gtOp.Right);
            Assert.AreEqual(5, ((NumericValue)gtOp.Right).GetValue());

            Assert.AreEqual(1, ifBody.StatementsToExecute.Count);
            Assert.IsInstanceOf<PrintStatement>(ifBody.StatementsToExecute[0]);
            var ifPrint = (PrintStatement)ifBody.StatementsToExecute[0];
            Assert.IsInstanceOf<TextValue>(ifPrint.Expression);
            Assert.AreEqual("a is greater than 5", ((TextValue)ifPrint.Expression).GetValue());

            // elif case
            var elifCondition = cases[1].Key;
            var elifBody = cases[1].Value;

            Assert.IsInstanceOf<GreaterThanOrEqualToOperator>(elifCondition);
            var gteOp = (GreaterThanOrEqualToOperator)elifCondition;

            Assert.IsInstanceOf<VariableExpression>(gteOp.Left);
            Assert.AreEqual("a", ((VariableExpression)gteOp.Left).Name);

            Assert.IsInstanceOf<NumericValue>(gteOp.Right);
            Assert.AreEqual(1, ((NumericValue)gteOp.Right).GetValue());

            Assert.AreEqual(1, elifBody.StatementsToExecute.Count);
            Assert.IsInstanceOf<PrintStatement>(elifBody.StatementsToExecute[0]);
            var elifPrint = (PrintStatement)elifBody.StatementsToExecute[0];
            Assert.IsInstanceOf<TextValue>(elifPrint.Expression);
            Assert.AreEqual("a is greater than or equal to 1", ((TextValue)elifPrint.Expression).GetValue());

            // else case
            var elseCondition = cases[2].Key;
            var elseBody = cases[2].Value;

            Assert.IsInstanceOf<LogicalValue>(elseCondition);
            Assert.IsTrue(((LogicalValue)elseCondition).GetValue());

            Assert.AreEqual(1, elseBody.StatementsToExecute.Count);
            Assert.IsInstanceOf<PrintStatement>(elseBody.StatementsToExecute[0]);
            var elsePrint = (PrintStatement)elseBody.StatementsToExecute[0];
            Assert.IsInstanceOf<TextValue>(elsePrint.Expression);
            Assert.AreEqual("a is less than 1", ((TextValue)elsePrint.Expression).GetValue());
        }

        [Test]
        public void TestClass()
        {
            var tokens = new List<WarScript.Token.Token>
            {
                T(TokenType.Keyword, "class", 1),
                T(TokenType.Variable, "Person", 1),
                T(TokenType.GroupDivider, "[", 1),
                T(TokenType.Variable, "name", 1),
                T(TokenType.GroupDivider, ",", 1),
                T(TokenType.Variable, "age", 1),
                T(TokenType.GroupDivider, "]", 1),
                T(TokenType.LineBreak, "\n", 1),
                T(TokenType.Keyword, "end", 2),
                T(TokenType.LineBreak, "\n", 2),
                T(TokenType.Variable, "person", 3),
                T(TokenType.Operator, "=", 3),
                T(TokenType.Operator, "new", 3),
                T(TokenType.Variable, "Person", 3),
                T(TokenType.GroupDivider, "[", 3),
                T(TokenType.Text, "Randy Marsh", 3),
                T(TokenType.GroupDivider, ",", 3),
                T(TokenType.Numeric, "45", 3),
                T(TokenType.GroupDivider, "]", 3),
                T(TokenType.LineBreak, "\n", 3),
                T(TokenType.Keyword, "print", 4),
                T(TokenType.Variable, "person", 4),
                T(TokenType.Operator, "::", 4),
                T(TokenType.Variable, "name", 4),
                T(TokenType.Operator, "+", 4),
                T(TokenType.Text, " is ", 4),
                T(TokenType.Operator, "+", 4),
                T(TokenType.Variable, "person", 4),
                T(TokenType.Operator, "::", 4),
                T(TokenType.Variable, "age", 4),
                T(TokenType.Operator, "+", 4),
                T(TokenType.Text, " years old", 4)
            };
            var statement = new CompositeStatement(_script, null, "testClass");
            StatementParser.Parse(_script, tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.AreEqual(2, statements.Count);

            // 1st statement: person = new Person["Randy Marsh", 45]
            Assert.IsInstanceOf<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsInstanceOf<AssignmentOperator>(expressionStatement.Expression);
            var assignStatement = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsInstanceOf<VariableExpression>(assignStatement.Left);
            var variableExpression = (VariableExpression)assignStatement.Left;
            Assert.AreEqual("person", variableExpression.Name);

            Assert.IsInstanceOf<ClassInstanceOperator>(assignStatement.Right);
            var instanceOperator = (ClassInstanceOperator)assignStatement.Right;

            // ClassExpression fields are private, so we verify the type
            // and validate behavior through execution in ExecutionTests instead
            Assert.IsInstanceOf<ClassExpression>(instanceOperator.Value);

            // Verify the class was registered in definitions
            var classDef = _script.DefinitionContext.GetScope().GetClass("Person");
            Assert.IsNotNull(classDef);
            Assert.AreEqual("Person", classDef.ClassDetails.Name);
            Assert.AreEqual(2, classDef.ClassDetails.Properties.Count);
            Assert.AreEqual("name", classDef.ClassDetails.Properties[0]);
            Assert.AreEqual("age", classDef.ClassDetails.Properties[1]);

            // 2nd statement: print expression
            Assert.IsInstanceOf<PrintStatement>(statements[1]);
            var printStatement = (PrintStatement)statements[1];
            Assert.IsInstanceOf<AdditionOperator>(printStatement.Expression);
        }

        [Test]
        public void TestComment()
        {
            var tokens = new List<WarScript.Token.Token>
            {
                T(TokenType.Comment, "# a = 5"),
                T(TokenType.LineBreak, "\n"),
                T(TokenType.Variable, "a"),
                T(TokenType.Operator, "="),
                T(TokenType.Numeric, "5"),
                T(TokenType.Comment, "# a is equal to 5")
            };
            var statement = new CompositeStatement(_script, null, "testComment");
            StatementParser.Parse(_script, tokens, statement);

            var statements = statement.StatementsToExecute;
            Assert.AreEqual(1, statements.Count);

            Assert.IsInstanceOf<ExpressionStatement>(statements[0]);
            var expressionStatement = (ExpressionStatement)statements[0];

            Assert.IsInstanceOf<AssignmentOperator>(expressionStatement.Expression);
            var assignStatement = (AssignmentOperator)expressionStatement.Expression;

            Assert.IsInstanceOf<VariableExpression>(assignStatement.Left);
            var variableExpression = (VariableExpression)assignStatement.Left;
            Assert.AreEqual("a", variableExpression.Name);

            Assert.IsInstanceOf<NumericValue>(assignStatement.Right);
            var numericValue = (NumericValue)assignStatement.Right;

            Assert.AreEqual(5, numericValue.GetValue());
        }
    }
}