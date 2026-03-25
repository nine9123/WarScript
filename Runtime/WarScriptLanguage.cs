#nullable enable

using System;
using System.Collections.Generic;
using WarScript.Bytecode;
using WarScript.Context;
using WarScript.Context.Definition;
using WarScript.Expression;
using WarScript.Expression.Value;
using WarScript.Statement;

namespace WarScript
{
    public class WarScriptLanguage
    {
        // ── Cached expression singletons for the parser ──
        public readonly ConstantExpression NullExpr;
        public readonly ConstantExpression TrueExpr;
        public readonly ConstantExpression FalseExpr;
        public readonly ThisExpression ThisExpr;

        // ── Consolidated halt flags for fast abort checking ──
        [Flags]
        public enum HaltFlag
        {
            None      = 0,
            Exception = 1,
            Return    = 2,
            Break     = 4,
            Next      = 8,
            Yield     = 16,
        }
        public HaltFlag HaltFlags;

        public readonly DefinitionContext DefinitionContext;
        public readonly MemoryContext MemoryContext;
        public readonly ExceptionContext ExceptionContext;
        public readonly ReturnContext ReturnContext;
        public readonly NextContext NextContext;
        public readonly BreakContext BreakContext;
        public readonly ClassInstanceContext ClassInstanceContext;
        public readonly IExpression DefaultStep;

        public readonly Func<string, string?>? FileResolver;
        public readonly Action<WarScriptLanguage, string>? Logger;

        internal readonly Dictionary<string, DefinitionScope> ImportCache = new();
        internal readonly HashSet<string> ImportStack = new();

        private readonly DefinitionScope _definitionScope;
        private readonly MemoryScope _memoryScope;

        public readonly string ScriptName;

        private readonly List<Token.Token> _tokens;

        private CompositeStatement? _cachedStatement;
        private CompiledFunction? _cachedCompiled;

        public readonly DefinitionScope GlobalDefinitionScope;
        public readonly MemoryScope GlobalMemoryScope;

        public MemoryScope UserMemoryScope => _memoryScope;

        // ── Coroutine support ──
        private readonly List<Coroutine> _coroutines = new();
        private int _nextCoroutineId = 1;

        public bool IsYielded { get; private set; }
        public YieldType YieldedType { get; private set; }
        public double YieldedWaitDuration { get; private set; }

        public WarScriptLanguage(
            string scriptName,
            string sourceCode,
            Func<string, string?>? fileResolver,
            Action<WarScriptLanguage, string>? logger)
        {
            ScriptName = scriptName;
            FileResolver = fileResolver;
            Logger = logger;

            NullExpr = new ConstantExpression(WarValue.Null);
            TrueExpr = new ConstantExpression(WarValue.True);
            FalseExpr = new ConstantExpression(WarValue.False);
            ThisExpr = new ThisExpression(this);

            DefinitionContext = new DefinitionContext(this);
            MemoryContext = new MemoryContext(this);
            ExceptionContext = new ExceptionContext(this);
            ReturnContext = new ReturnContext();
            NextContext = new NextContext();
            BreakContext = new BreakContext();
            ClassInstanceContext = new ClassInstanceContext();
            DefaultStep = new ConstantExpression(WarValue.FromNumeric(1.0));

            _tokens = LexicalParser.Parse(sourceCode);

            var nativeDefinitionScope = DefinitionContext.NewScope();
            var nativeMemoryScope = MemoryContext.NewScope();
            nativeMemoryScope.Poolable = false;

            DefinitionContext.PushScope(nativeDefinitionScope);
            MemoryContext.PushScope(nativeMemoryScope);

            GlobalDefinitionScope = nativeDefinitionScope;
            GlobalMemoryScope = nativeMemoryScope;

            _definitionScope = DefinitionContext.NewScope();
            _memoryScope = MemoryContext.NewScope();
            _memoryScope.Poolable = false;

            DefinitionContext.EndScope();
            MemoryContext.EndScope();
        }

        public void Run()
        {
            RunBytecode();
            return;
            
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            try
            {
                if (_cachedStatement == null)
                {
                    var statement = new CompositeStatement(this, null, ScriptName);
                    StatementParser.Parse(this, _tokens, statement);
                    _cachedStatement = statement;
                }

                _cachedStatement.Execute();
            }
            finally
            {
                DefinitionContext.EndScope();
                MemoryContext.EndScope();

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();

                HaltFlags = HaltFlag.None;
            }
        }

        /// <summary>
        /// Parse the script (if not cached), compile to bytecode, and execute
        /// in the bytecode VM. Drop-in replacement for <see cref="Run"/>.
        /// </summary>
        public void RunBytecode()
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            try
            {
                // Parse (reuse cached AST)
                if (_cachedStatement == null)
                {
                    var statement = new CompositeStatement(this, null, ScriptName);
                    StatementParser.Parse(this, _tokens, statement);
                    _cachedStatement = statement;
                }

                // Compile (reuse cached bytecode)
                if (_cachedCompiled == null)
                    _cachedCompiled = Compiler.CompileScript(this, _cachedStatement, _definitionScope);

                // Execute in the VM
                var vm = new WarVM(this);
                vm.Run(_cachedCompiled);
            }
            finally
            {
                DefinitionContext.EndScope();
                MemoryContext.EndScope();

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();

                HaltFlags = HaltFlag.None;
            }
        }

        public FunctionDefinition? GetFunction(string functionName, int arguments)
        {
            return _definitionScope.GetFunction(functionName, arguments);
        }

        public void Call(FunctionDefinition function, params WarValue[] arguments)
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);
            MemoryContext.PushScope(MemoryContext.NewScope());

            try
            {
                var details = function.Details;
                for (var i = 0; i < details.Arguments.Count; i++)
                {
                    MemoryContext.GetScope().SetLocal(
                        details.Arguments[i],
                        i < arguments.Length ? arguments[i] : WarValue.Null);
                }

                function.Statement.Execute();
            }
            finally
            {
                MemoryContext.EndScope();
                DefinitionContext.EndScope();
                MemoryContext.EndScope();
                ReturnContext.Reset();
                HaltFlags &= ~HaltFlag.Return;

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();

                HaltFlags = HaltFlag.None;
            }
        }

        public bool HasFunction(string functionName, int argumentsSize)
        {
            return _definitionScope.ContainsFunction(functionName, argumentsSize);
        }

        /// <summary>
        /// Convenience method: raises an exception and returns a null WarValue.
        /// Used by operators to combine error raising and return in one line.
        /// </summary>
        public WarValue RaiseException(string message)
        {
            return ExceptionContext.RaiseException(message);
        }

        public void SetYielded(YieldType type, double waitDuration)
        {
            IsYielded = true;
            HaltFlags |= HaltFlag.Yield;
            YieldedType = type;
            YieldedWaitDuration = waitDuration;
        }

        public void ClearYield()
        {
            IsYielded = false;
            HaltFlags &= ~HaltFlag.Yield;
            YieldedType = YieldType.NextTick;
            YieldedWaitDuration = 0;
        }

        public int StartCoroutine(string functionName, WarValue[] args, bool loop = false)
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
                args ?? Array.Empty<WarValue>(), loop, id);

            _coroutines.Add(coroutine);
            coroutine.Resume();

            return id;
        }

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

        public void StopAllCoroutines() => _coroutines.Clear();

        public int TickCoroutines(double dt)
        {
            for (var i = _coroutines.Count - 1; i >= 0; i--)
            {
                var co = _coroutines[i];
                if (!co.IsReady(dt)) continue;
                co.Resume();
                if (co.IsComplete) _coroutines.RemoveAt(i);
            }
            return _coroutines.Count;
        }

        public int ActiveCoroutineCount => _coroutines.Count;
    }
}
