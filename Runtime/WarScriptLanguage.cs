#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript
{
    public class WarScriptLanguage
    {
        public readonly NullValue Null;
        public readonly ThisValue This;
        public readonly DefinitionContext DefinitionContext;
        public readonly MemoryContext MemoryContext;
        public readonly ExceptionContext ExceptionContext;
        public readonly ReturnContext ReturnContext;
        public readonly NextContext NextContext;
        public readonly BreakContext BreakContext;
        public readonly ClassInstanceContext ClassInstanceContext;
        public readonly IExpression DefaultStep;

        /// <summary>
        /// Host-provided callback to read source code from a file path.
        /// Receives the path string from the import statement and returns the file contents.
        /// Null if imports are not supported.
        /// </summary>
        public readonly Func<string, string?>? FileResolver;

        public readonly Action<WarScriptLanguage, string>? Logger;

        /// <summary>
        /// Cache of already-imported files keyed by resolved path.
        /// Prevents re-parsing the same file when imported from multiple locations.
        /// </summary>
        internal readonly Dictionary<string, DefinitionScope> ImportCache = new();

        /// <summary>
        /// Tracks files currently being imported for circular dependency detection.
        /// </summary>
        internal readonly HashSet<string> ImportStack = new();

        private readonly DefinitionScope _definitionScope;
        private readonly MemoryScope _memoryScope;

        public readonly string ScriptName;

        /// <param name="scriptName">Name of the script (used in error messages)</param>
        /// <param name="sourceCode">Source code to execute</param>
        /// <param name="setupGlobalScope">Callback to register native functions/classes</param>
        /// <param name="fileResolver">Callback to read imported files by path. Null disables imports</param>
        /// <param name="logger">Callback to log print messages</param>
        public WarScriptLanguage(
            string scriptName,
            string sourceCode,
            Action<DefinitionScope> setupGlobalScope,
            Func<string, string?>? fileResolver,
            Action<WarScriptLanguage, string>? logger)
        {
            ScriptName = scriptName;
            FileResolver = fileResolver;
            Logger = logger;
            
            Null = new NullValue(this);
            This = new ThisValue(this);
            DefinitionContext = new DefinitionContext(this);
            MemoryContext = new MemoryContext(this);
            ExceptionContext = new ExceptionContext(this);
            ReturnContext = new ReturnContext();
            NextContext = new NextContext();
            BreakContext = new BreakContext();
            ClassInstanceContext = new ClassInstanceContext();
            DefaultStep = new NumericValue(this, 1.0);
            
            var tokens = LexicalParser.Parse(sourceCode);

            // Native scope — holds native bindings from setupGlobalScope
            var nativeDefinitionScope = DefinitionContext.NewScope();
            var nativeMemoryScope = MemoryContext.NewScope();

            DefinitionContext.PushScope(nativeDefinitionScope);
            MemoryContext.PushScope(nativeMemoryScope);

            setupGlobalScope.Invoke(DefinitionContext.GetScope());

            // User scope — child of native scope, so user definitions shadow natives
            _definitionScope = DefinitionContext.NewScope();
            _memoryScope = MemoryContext.NewScope();

            DefinitionContext.EndScope();
            MemoryContext.EndScope();

            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            try
            {
                var statement = new CompositeStatement(this, null, scriptName);
                StatementParser.Parse(this, tokens, statement);
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

        public FunctionDefinition? GetFunction(string functionName, int arguments)
        {
            return _definitionScope.GetFunction(functionName, arguments);
        }
        
        public void Call(FunctionDefinition function, params IValue[] arguments)
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            // Create a nested scope for the function arguments,
            // same as FunctionExpression.Evaluate does
            MemoryContext.PushScope(MemoryContext.NewScope());
            
            try
            {
                var details = function.Details;
                for (var i = 0; i < details.Arguments.Count; i++)
                {
                    MemoryContext.GetScope().SetLocal(
                        details.Arguments[i],
                        i < arguments.Length ? arguments[i] : Null
                    );
                }
                
                function.Statement.Execute();
            }
            finally
            {
                MemoryContext.EndScope(); // function argument scope
                DefinitionContext.EndScope();
                MemoryContext.EndScope();
                ReturnContext.Reset();

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();
            }
        }

        public bool HasFunction(string functionName, int argumentsSize)
        {
            return _definitionScope.ContainsFunction(functionName, argumentsSize);
        }
    }
}