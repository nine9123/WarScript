using WarScript;
using WarScript.Token;

namespace ToyLanguage.Tests
{
    public class LexicalParserTest
    {
        [Fact]
        public void TestPrint()
        {
            var source = "print \"Hello World\"";
            var tokens = LexicalParser.Parse(source);

            Assert.Equal(2, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Keyword, tokens[count].Type);
            Assert.Equal("print", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal("Hello World", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);
        }

        [Fact]
        public void TestInput()
        {
            var source = "input a";
            var tokens = LexicalParser.Parse(source);

            Assert.Equal(2, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Keyword, tokens[count].Type);
            Assert.Equal("input", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("a", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);
        }

        [Fact]
        public void TestAssignment()
        {
            var source = "a = 2 + 5";
            var tokens = LexicalParser.Parse(source);

            Assert.Equal(5, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Variable, tokens[count].Type);
            Assert.Equal("a", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("=", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("2", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("+", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("5", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);
        }

        [Fact]
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

            Assert.Equal(22, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Keyword, tokens[count].Type);
            Assert.Equal("if", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("a", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal(">", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("5", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("print", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal("a is greater than 5", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("elif", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("a", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal(">=", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("1", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("print", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal("a is greater than or equal to 1", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("else", tokens[count].Value);
            Assert.Equal(5, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(5, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("print", tokens[count].Value);
            Assert.Equal(6, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal("a is less than 1", tokens[count].Value);
            Assert.Equal(6, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(6, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("end", tokens[count].Value);
            Assert.Equal(7, tokens[count].RowNumber);
        }

        [Fact]
        public void TestClass()
        {
            var source = "class Person [ name, age ]\n" +
                         "end\n" +
                         "person = new Person[\"Randy Marsh\", 45]\n" +
                         "print person :: name + \" is \" + person :: age + \" years old\"";
            var tokens = LexicalParser.Parse(source);

            Assert.Equal(32, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Keyword, tokens[count].Type);
            Assert.Equal("class", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("Person", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal("[", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("name", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal(",", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("age", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal("]", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("end", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("person", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("=", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("new", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("Person", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal("[", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal("Randy Marsh", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal(",", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("45", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.GroupDivider, tokens[++count].Type);
            Assert.Equal("]", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(3, tokens[count].RowNumber);

            Assert.Equal(TokenType.Keyword, tokens[++count].Type);
            Assert.Equal("print", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("person", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("::", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("name", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("+", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal(" is ", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("+", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("person", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("::", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("age", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("+", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);

            Assert.Equal(TokenType.Text, tokens[++count].Type);
            Assert.Equal(" years old", tokens[count].Value);
            Assert.Equal(4, tokens[count].RowNumber);
        }

        [Fact]
        public void TestComment()
        {
            var source = "# a = 5\n" +
                         "a = 5 # a is equal to 5";
            var tokens = LexicalParser.Parse(source);

            Assert.Equal(6, tokens.Count);

            var count = 0;
            Assert.Equal(TokenType.Comment, tokens[count].Type);
            Assert.Equal("# a = 5", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.LineBreak, tokens[++count].Type);
            Assert.Equal("\n", tokens[count].Value);
            Assert.Equal(1, tokens[count].RowNumber);

            Assert.Equal(TokenType.Variable, tokens[++count].Type);
            Assert.Equal("a", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Operator, tokens[++count].Type);
            Assert.Equal("=", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Numeric, tokens[++count].Type);
            Assert.Equal("5", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);

            Assert.Equal(TokenType.Comment, tokens[++count].Type);
            Assert.Equal("# a is equal to 5", tokens[count].Value);
            Assert.Equal(2, tokens[count].RowNumber);
        }
    }
}
