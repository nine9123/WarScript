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
        private WarVM? _cachedVM;

        public readonly DefinitionScope GlobalDefinitionScope;
        public readonly MemoryScope GlobalMemoryScope;

        public MemoryScope UserMemoryScope => _memoryScope;

        // ── Coroutine support ──
        // NOTE: Coroutines still use tree-walk execution because they split
        // functions at yield points and execute each segment individually.
        // Full bytecode coroutines would require VM suspend/resume (saving
        // IP + stack state), which is a separate feature.
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

        // ────────────────────────────────────────────────────────
        //  Parse + compile (lazy, cached)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Ensure the script has been parsed and compiled to bytecode.
        /// Called automatically by Run() and Call(). Safe to call multiple
        /// times — parsing and compilation are cached.
        /// Must be called with _definitionScope pushed on DefinitionContext.
        /// </summary>
        private void EnsureCompiled()
        {
            if (_cachedStatement == null)
            {
                var statement = new CompositeStatement(this, null, ScriptName);
                StatementParser.Parse(this, _tokens, statement);
                _cachedStatement = statement;
            }

            if (_cachedCompiled == null)
                _cachedCompiled = Compiler.CompileScript(this, _cachedStatement, _definitionScope);
        }

        /// <summary>
        /// Returns the cached VM instance, creating it once on first use.
        /// The VM resets its own internal state on each Run/RunFunction call,
        /// so the same instance is safe to reuse across all invocations.
        /// </summary>
        private WarVM EnsureVM()
        {
            return _cachedVM ??= new WarVM(this);
        }

        // ────────────────────────────────────────────────────────
        //  Run — execute the full script via bytecode VM
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Parse, compile, and execute the script. On first call this
        /// parses the source and compiles all functions to bytecode.
        /// Subsequent calls reuse the cached bytecode.
        /// </summary>
        public void Run()
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);

            try
            {
                EnsureCompiled();
                var vm = EnsureVM();
                vm.Run(_cachedCompiled!);
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

        // ────────────────────────────────────────────────────────
        //  Call — host invokes a WarScript function (tick, events)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Call a WarScript function from the host (C#/Unity).
        /// Uses bytecode when available (always after Run() has been called).
        /// This is the hot path for game integration — tick loops, event handlers.
        /// </summary>
        public void Call(FunctionDefinition function, params WarValue[] arguments)
        {
            if (function.Compiled != null)
            {
                CallBytecode(function, arguments);
                return;
            }

            // Fallback: tree-walk for functions not yet compiled
            // (e.g. Call() before Run(), or native function wrappers)
            CallTreeWalk(function, arguments);
        }

        private void CallBytecode(FunctionDefinition function, WarValue[] arguments)
        {
            DefinitionContext.PushScope(_definitionScope);
            MemoryContext.PushScope(_memoryScope);
            // Push a scope for function-local variables accessed via SetGlobal.
            // Parented to _memoryScope, matching the VM's OP_CALL behavior.
            MemoryContext.PushScope(MemoryContext.NewScope(UserMemoryScope));

            try
            {
                var vm = EnsureVM();
                vm.RunFunction(function.Compiled!, arguments);
            }
            finally
            {
                MemoryContext.EndScope();  // function-local scope
                DefinitionContext.EndScope();
                MemoryContext.EndScope();
                ReturnContext.Reset();
                HaltFlags &= ~HaltFlag.Return;

                if (ExceptionContext.IsRaised())
                    ExceptionContext.PrintStackTrace();

                HaltFlags = HaltFlag.None;
            }
        }

        private void CallTreeWalk(FunctionDefinition function, WarValue[] arguments)
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

                function.Statement!.Execute();
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

        // ────────────────────────────────────────────────────────
        //  Function lookup
        // ────────────────────────────────────────────────────────

        public FunctionDefinition? GetFunction(string functionName, int arguments)
        {
            return _definitionScope.GetFunction(functionName, arguments);
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

        // ────────────────────────────────────────────────────────
        //  Yield support
        // ────────────────────────────────────────────────────────

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

        // ────────────────────────────────────────────────────────
        //  Coroutine support (tree-walk — see note at field decl)
        // ────────────────────────────────────────────────────────

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
