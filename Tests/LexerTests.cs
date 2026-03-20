using NUnit.Framework;
using WarScript;
using WarScript.Token;

namespace Tests
{
    [TestFixture]
    public class LexerTests
    {
        [Test]
        public void Numeric_Token_Parsed()
        {
            var tokens = LexicalParser.Parse("42");
            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(TokenType.Numeric, tokens[0].Type);
            Assert.AreEqual("42", tokens[0].Value);
        }
        
        [Test]
        public void Operator_Tokens_All_Recognized()
        {
            var ops = new[] { "+", "-", "*", "/", "==", "!=", "<=", ">=", "<", ">", "**", "//" };
            foreach (var op in ops)
            {
                var tokens = LexicalParser.Parse($"1 {op} 2");
                Assert.IsTrue(tokens.Exists(t => t.Type == TokenType.Operator && t.Value == op),
                    $"Operator '{op}' not recognized");
            }
        }
        
        [Test]
        public void String_Literal_Strips_Quotes()
        {
            var tokens = LexicalParser.Parse("\"hello world\"");
            Assert.AreEqual("hello world", tokens[0].Value);
        }
        
        [Test]
        public void TestPrint()
        {
            var source = "print \"Hello World\"";
            var tokens = LexicalParser.Parse(source);

            Assert.AreEqual(2, tokens.Count);

            var count = 0;
            Assert.AreEqual(TokenType.Keyword, tokens[count].Type);
            Assert.AreEqual("print", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual("Hello World", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);
        }

        [Test]
        public void TestAssignment()
        {
            var source = "a = 2 + 5";
            var tokens = LexicalParser.Parse(source);

            Assert.AreEqual(5, tokens.Count);

            var count = 0;
            Assert.AreEqual(TokenType.Variable, tokens[count].Type);
            Assert.AreEqual("a", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("=", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("2", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("+", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("5", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);
        }

        [Test]
        public void TestCondition()
        {
            var source = "if a > 5\n" +
                         "    print \"a is greater than 5\"\n" +
                         "elif a >= 1\n" +
                         "    print \"a is greater than or equal to 1\"\n" +
                         "else\n" +
                         "    print \"a is less than 1\"\n" +
                         "end";
            var tokens = LexicalParser.Parse(source);

            Assert.AreEqual(22, tokens.Count);

            var count = 0;
            Assert.AreEqual(TokenType.Keyword, tokens[count].Type);
            Assert.AreEqual("if", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("a", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual(">", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("5", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("print", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual("a is greater than 5", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("elif", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("a", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual(">=", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("1", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("print", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual("a is greater than or equal to 1", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("else", tokens[count].Value);
            Assert.AreEqual(5, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(5, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("print", tokens[count].Value);
            Assert.AreEqual(6, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual("a is less than 1", tokens[count].Value);
            Assert.AreEqual(6, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(6, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("end", tokens[count].Value);
            Assert.AreEqual(7, tokens[count].RowNumber);
        }

        [Test]
        public void TestClass()
        {
            var source = "class Person [ name, age ]\n" +
                         "end\n" +
                         "person = new Person[\"Randy Marsh\", 45]\n" +
                         "print person :: name + \" is \" + person :: age + \" years old\"";
            var tokens = LexicalParser.Parse(source);

            Assert.AreEqual(32, tokens.Count);

            var count = 0;
            Assert.AreEqual(TokenType.Keyword, tokens[count].Type);
            Assert.AreEqual("class", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("Person", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual("[", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("name", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual(",", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("age", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual("]", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("end", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("person", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("=", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("new", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("Person", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual("[", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual("Randy Marsh", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual(",", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("45", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.GroupDivider, tokens[++count].Type);
            Assert.AreEqual("]", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(3, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Keyword, tokens[++count].Type);
            Assert.AreEqual("print", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("person", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("::", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("name", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("+", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual(" is ", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("+", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("person", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("::", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("age", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("+", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Text, tokens[++count].Type);
            Assert.AreEqual(" years old", tokens[count].Value);
            Assert.AreEqual(4, tokens[count].RowNumber);
        }

        [Test]
        public void TestComment()
        {
            var source = "# a = 5\n" +
                         "a = 5 # a is equal to 5";
            var tokens = LexicalParser.Parse(source);

            Assert.AreEqual(6, tokens.Count);

            var count = 0;
            Assert.AreEqual(TokenType.Comment, tokens[count].Type);
            Assert.AreEqual("# a = 5", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.LineBreak, tokens[++count].Type);
            Assert.AreEqual("\n", tokens[count].Value);
            Assert.AreEqual(1, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Variable, tokens[++count].Type);
            Assert.AreEqual("a", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Operator, tokens[++count].Type);
            Assert.AreEqual("=", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Numeric, tokens[++count].Type);
            Assert.AreEqual("5", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);

            Assert.AreEqual(TokenType.Comment, tokens[++count].Type);
            Assert.AreEqual("# a is equal to 5", tokens[count].Value);
            Assert.AreEqual(2, tokens[count].RowNumber);
        }
        
        [Test]
        public void ThisBeforeClosingBracket()
        {
            var tokens = LexicalParser.Parse("arr_remove[objects, this]");
            var thisToken = tokens.Find(t => t.Value == "this");
            Assert.IsNotNull(thisToken);
            Assert.AreEqual(TokenType.This, thisToken.Type,
                "this before ] was lexed as Variable instead of This");
        }

        [Test]
        public void NullBeforeClosingBracket()
        {
            var tokens = LexicalParser.Parse("call[null]");
            var nullToken = tokens.Find(t => t.Value == "null");
            Assert.IsNotNull(nullToken);
            Assert.AreEqual(TokenType.Null, nullToken.Type,
                "null before ] was lexed as Variable instead of Null");
        }

        [Test]
        public void KeywordsBeforeClosingBracket()
        {
            var tokens = LexicalParser.Parse("if this and true]");
            var andToken = tokens.Find(t => t.Value == "and");
            Assert.AreEqual(TokenType.Operator, andToken.Type);
        }
    }
}
