using System;
using System.IO;
using WarScript.Lexer;
using WarScript.Syntax;

namespace WarScript
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var sourceCode = File.ReadAllLines("/Users/coder/RiderProjects/WarScript/WarScript/test_script.wscript");

            var lexicalParser = new LexicalParser(sourceCode);
            var tokens = lexicalParser.Parse();

            foreach (var token in tokens)
            {
                Console.WriteLine(token);
            }
            
            var statementParser = new StatementParser(tokens);
            var statement = statementParser.Parse();

            statement.Execute();
        }
    }
}