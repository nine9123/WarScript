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

        private readonly List<Token.Token> _tokens;

        public readonly DefinitionScope GlobalDefinitionScope;
        public readonly MemoryScope GlobalMemoryScope;
        
        /// <summary>
        /// The user-level memory scope where script globals live.
        /// Standalone function calls parent their scope here so that
        /// recursive calls get isolated locals while retaining global access.
        /// </summary>
        public MemoryScope UserMemoryScope => _memoryScope;
        
        // ── Coroutine support ──
        private readonly List<Coroutine> _coroutines = new();
        private int _nextCoroutineId = 1;

        // Yield state — set by YieldStatement.Execute(), read by Coroutine.Resume()
        public bool IsYielded { get; private set; }
        public YieldType YieldedType { get; private set; }
        public double YieldedWaitDuration { get; private set; }
        
        /// <param name="scriptName">Name of the script (used in error messages)</param>
        /// <param name="sourceCode">Source code to execute</param>
        /// <param name="fileResolver">Callback to read imported files by path. Null disables imports</param>
        /// <param name="logger">Callback to log print messages</param>
        public WarScriptLanguage(
            string scriptName,
            string sourceCode,
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
            
            _tokens = LexicalParser.Parse(sourceCode);

            // Native scope: holds native bindings
            var nativeDefinitionScope = DefinitionContext.NewScope();
            var nativeMemoryScope = MemoryContext.NewScope();

            DefinitionContext.PushScope(nativeDefinitionScope);
            MemoryContext.PushScope(nativeMemoryScope);

            GlobalDefinitionScope = nativeDefinitionScope;
            GlobalMemoryScope = nativeMemoryScope;

            // User scope: child of native scope. User definitions shadow natives
            _definitionScope = DefinitionContext.NewScope();
            _memoryScope = MemoryContext.NewScope();

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        public void Run()
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            try
            {
                var statement = new CompositeStatement(this, null, ScriptName);
                StatementParser.Parse(this, _tokens, statement);
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
        
        public void SetYielded(YieldType type, double waitDuration)
        {
            IsYielded = true;
            YieldedType = type;
            YieldedWaitDuration = waitDuration;
        }

        public void ClearYield()
        {
            IsYielded = false;
            YieldedType = YieldType.NextTick;
            YieldedWaitDuration = 0;
        }
        
        /// <summary>
        /// Starts a coroutine from a named function. Returns a coroutine ID.
        /// The first segment executes immediately.
        /// </summary>
        public int StartCoroutine(string functionName, IValue[] args, bool loop = false)
        {
            var argCount = args?.Length ?? 0;
            var function = _definitionScope.GetFunction(functionName, argCount);
            if (function == null)
            {
                ExceptionContext.RaiseException(
                    $"Coroutine function '{functionName}' with {argCount} arguments is not defined");
                return -1;
            }

            var id = _nextCoroutineId++;
            var coroutine = new Coroutine(
                this, function, _definitionScope, _memoryScope,
                args ?? System.Array.Empty<IValue>(), loop, id);

            _coroutines.Add(coroutine);

            // Execute first segment immediately
            coroutine.Resume();

            return id;
        }

        /// <summary>
        /// Stops a coroutine by ID.
        /// </summary>
        public bool StopCoroutine(int id)
        {
            for (var i = _coroutines.Count - 1; i >= 0; i--)
            {
                if (_coroutines[i].Id == id)
                {
                    _coroutines.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Stops all active coroutines.
        /// </summary>
        public void StopAllCoroutines()
        {
            _coroutines.Clear();
        }

        /// <summary>
        /// Called by the engine each frame. Checks yield conditions and
        /// resumes ready coroutines. Returns the number of active coroutines.
        /// </summary>
        public int TickCoroutines(double dt)
        {
            for (var i = _coroutines.Count - 1; i >= 0; i--)
            {
                var co = _coroutines[i];

                if (!co.IsReady(dt))
                    continue;

                co.Resume();

                if (co.IsComplete)
                    _coroutines.RemoveAt(i);
            }

            return _coroutines.Count;
        }

        /// <summary>
        /// Number of active coroutines.
        /// </summary>
        public int ActiveCoroutineCount => _coroutines.Count;
    }
}