using System.Collections.Generic;
using System.IO;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Statement;
using WarScript.Token;

namespace WarScript
{
    public class WarScriptLanguage
    {
        public void Execute(string path)
        {
            var sourceCode = File.ReadAllText(path);
            var tokens = LexicalParser.Parse(sourceCode);

            DefinitionContext.PushScope(DefinitionContext.NewScope());
            MemoryContext.PushScope(MemoryContext.NewScope());
            try
            {
                var fileName = Path.GetFileName(path);
                var statement = new CompositeStatement(null, fileName);
                StatementParser.Parse(tokens, statement);
                statement.Execute();
            }
            finally
            {
                DefinitionContext.EndScope();
                MemoryContext.EndScope();

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();
            }
        }
    }
}