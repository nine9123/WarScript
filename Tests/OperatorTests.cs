using NUnit.Framework;
using WarScript.Expression.Operator;
using WarScript.Expression.Operator.Extensions;

namespace Tests
{
    [TestFixture]
    public class OperatorTests
    {
        [TestCase("+",   Operator.Addition)]
        [TestCase("-",   Operator.Subtraction)]
        [TestCase("*",   Operator.Multiplication)]
        [TestCase("/",   Operator.Division)]
        [TestCase("%",   Operator.Modulo)]
        [TestCase("==",  Operator.Equals)]
        [TestCase("!=",  Operator.NotEquals)]
        [TestCase("+=",  Operator.AdditionAssignment)]
        [TestCase("/=",  Operator.DivisionAssignment)]
        [TestCase("-=",  Operator.SubtractionAssignment)]
        [TestCase("*=",  Operator.MultiplicationAssignment)]
        [TestCase("<",   Operator.LessThan)]
        [TestCase("<=",  Operator.LessThanOrEqualTo)]
        [TestCase(">",   Operator.GreaterThan)]
        [TestCase(">=",  Operator.GreaterThanOrEqualTo)]
        [TestCase("=",   Operator.Assignment)]
        [TestCase("!",   Operator.Not)]
        [TestCase("(",   Operator.LeftParen)]
        [TestCase(")",   Operator.RightParen)]
        [TestCase("::",  Operator.ClassProperty)]
        [TestCase("<<",  Operator.ArrayAppend)]
        [TestCase("new", Operator.ClassInstance)]
        [TestCase("and", Operator.LogicalAnd)]
        [TestCase("or",  Operator.LogicalOr)]
        [TestCase("as",  Operator.ClassCast)]
        [TestCase("is",  Operator.ClassInstanceOf)]
        public void ToOperator_AllSymbols(string input, Operator expected)
        {
            Assert.AreEqual(expected, input.ToOperator());
        }

        [TestCase(":: new")]
        [TestCase("::  new")]
        [TestCase("::   new")]
        public void ToOperator_NestedClassInstance_VariableWhitespace(string input)
        {
            Assert.AreEqual(Operator.NestedClassInstance, input.ToOperator());
        }

        [Test]
        public void ToOperator_InvalidInput_Throws()
        {
            Assert.Throws<System.Exception>(() => "???".ToOperator());
        }
    }
}
