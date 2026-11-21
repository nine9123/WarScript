#nullable enable

using FixMath;
using System;
using System.Collections.Generic;
using System.IO;
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

        public readonly DefinitionContext DefinitionContext;
        public readonly ExceptionContext ExceptionContext;
        public readonly IExpression DefaultStep;

        public readonly Func<string, string?>? FileResolver;

        /// <summary>
        /// Optional host hook returning the precompiled bytecode (the bytes
        /// written by <see cref="SaveBytecode"/>) for an <c>import</c> path,
        /// or null when the host has none for it.
        ///
        /// When it answers, the import skips the lexer, parser and compiler
        /// the way <see cref="LoadBytecode"/> does for the entry point — which
        /// is where nearly all of a script's work usually is, since a project
        /// typically reaches most of its code through <c>import</c>.
        ///
        /// Bytecode that will not load (stale, truncated, a version from a
        /// newer WarScript) is reported through <see cref="Logger"/> and the
        /// import falls back to <see cref="FileResolver"/>, so a host that
        /// ships both keeps working on either.
        /// </summary>
        public readonly Func<string, byte[]?>? BytecodeResolver;

        public readonly Action<WarScriptLanguage, string>? Logger;

        /// <summary>
        /// A host-chosen name for this instance (PROP-4, plan T2.8), e.g. the
        /// unit or room it scripts, since many instances may run the same
        /// script. Every <see cref="ScriptError"/> the instance reports
        /// carries it (<see cref="ScriptError.InstanceLabel"/>, as it was when
        /// the error was reported), and every line the runtime itself sends to
        /// <see cref="Logger"/> starts with <c>[label] </c>. <c>print</c>
        /// output is the script's, not the runtime's, and is passed on
        /// unchanged. Null (the default) adds nothing.
        /// </summary>
        public string? InstanceLabel { get; set; }

        /// <summary>
        /// Sends a line the runtime emits (not <c>print</c> output) to
        /// <see cref="Logger"/>, prefixed with <c>[</c><see cref="InstanceLabel"/><c>] </c>
        /// when one is set (PROP-4).
        /// </summary>
        internal void Log(string line)
        {
            if (Logger == null) return;
            var label = InstanceLabel;
            Logger(this, label == null ? line : "[" + label + "] " + line);
        }

        /// <summary>FNV-1a state over every line printed so far (see <see cref="ComputeStateHash"/>).</summary>
        private ulong _printedHash = StateHash.Offset;

        /// <summary>
        /// The paths of the imports whose top-level code has run in this
        /// instance (each runs once per instance, 5.8). Cleared by
        /// <see cref="Reload"/> and <see cref="LoadBytecode"/>.
        /// </summary>
        internal readonly HashSet<string> ImportCache = new(System.StringComparer.Ordinal);

        /// <summary>
        /// The imports <see cref="Compile"/> or <see cref="LoadBytecode"/>
        /// resolved and compiled (T3.7, 5.8, DEC-7), by path, a bytecode
        /// file's from its module header (format 10). Cleared with the
        /// definitions.
        /// </summary>
        internal readonly Dictionary<string, ImportedModule> ResolvedImports = new();

        /// <summary>
        /// <see cref="ResolvedImports"/> in the order they were resolved, a
        /// depth-first post-order over the imports in textual order: the
        /// order their top-level code runs in, before the script's (5.8,
        /// T3.7). Kept with <see cref="ResolvedImports"/>.
        /// </summary>
        internal readonly List<ImportedModule> ImportOrder = new();

        /// <summary>
        /// While <see cref="Compile"/> parses a file: the definitions of every
        /// import resolved so far, latest last, where a named-argument call
        /// looks its function up when the scope chain has none of that name
        /// (<see cref="FindImportedFunction"/>). Null otherwise.
        /// </summary>
        private List<DefinitionScope>? _parsingImports;

        /// <summary>
        /// The paths the script imports, in order, each once (T3.7): read by
        /// <see cref="Compile"/> or from a loaded module's header, and
        /// written into the module header by <see cref="SaveBytecode"/>.
        /// </summary>
        private IReadOnlyList<string> _imports = System.Array.Empty<string>();

        /// <summary>An import resolved by <see cref="Compile"/> or <see cref="LoadBytecode"/>: its path, top-level code and definitions.</summary>
        internal sealed class ImportedModule
        {
            public readonly string Path;
            public readonly CompiledFunction TopLevel;
            public readonly DefinitionScope Definitions;

            public ImportedModule(string path, CompiledFunction topLevel, DefinitionScope definitions)
            {
                Path = path;
                TopLevel = topLevel;
                Definitions = definitions;
            }
        }

        /// <summary>Records a resolved import, after its own imports (<see cref="ImportOrder"/>).</summary>
        private void AddResolvedImport(ImportedModule module)
        {
            ResolvedImports[module.Path] = module;
            ImportOrder.Add(module);
        }

        private void ClearResolvedImports()
        {
            ResolvedImports.Clear();
            ImportOrder.Clear();
        }

        /// <summary>
        /// Names declared with <c>const</c> or generated by <c>enum</c>.
        /// The compiler rejects assignment to these names.
        /// </summary>
        internal readonly HashSet<string> ConstantNames = new();

        /// <summary>
        /// Forgets the constants the script's files declared, keeping the
        /// host's (<c>[WsConst]</c>, <c>[WsEnum]</c>, a native manifest's
        /// <c>const</c>/<c>enum</c>: a constant name that is a host global),
        /// which the host registered once, before the first compile.
        /// </summary>
        private void ClearScriptConstantNames() =>
            ConstantNames.RemoveWhere(name => !GlobalMemoryScope.HasLocal(name));

        private readonly DefinitionScope _definitionScope;

        /// <summary>
        /// The script's definitions: its own functions and classes and, as
        /// imported ones, those of every file it imports (one namespace for
        /// the module set, T3.7, 5.8).
        /// </summary>
        internal DefinitionScope Definitions => _definitionScope;
        private readonly MemoryScope _memoryScope;

        /// <summary>
        /// Incremented whenever a function is added to a definition scope of
        /// this instance or a scope is cleared (<see cref="Reload"/>,
        /// <see cref="LoadBytecode"/>, imports, <c>AddFunction</c>), so a
        /// <see cref="FunctionHandle"/> knows when to look its function up
        /// again (plan T2.6).
        /// </summary>
        internal int DefinitionsVersion { get; private set; }

        internal void DefinitionsChanged() => DefinitionsVersion = unchecked(DefinitionsVersion + 1);

        public readonly string ScriptName;

        // The source Compile() lexes, parses and compiles (plan T2.2): set by
        // the constructor and Reload, not lexed until then.
        private string _sourceCode;

        // Compile()'s cached answer (null: not compiled yet) and, for a
        // failed compile, the error the v1 entry points still throw.
        private CompileResult? _compileResult;
        private Exception.SyntaxException? _compileError;
        private CompiledFunction? _cachedCompiled;

        // While Compile() runs: the warnings the compiler reports (T3.8,
        // through Warn); null otherwise.
        private List<CompileDiagnostic>? _compileWarnings;

        // While Compile() runs: every `new` the compiler emitted (T3.8,
        // PROP-29), checked once the whole module set is compiled
        // (CheckInstantiations); null otherwise.
        private List<(string Class, int Arguments, bool Nested, string? File, string? Source, int Line, int Column)>? _instantiations;

        // While Compile() runs: every `obj :: x` / `obj :: m [...]` the
        // compiler emitted (T3.8, PROP-29; Enum: the left operand's name when
        // it is a bare global read), and the members of every enum declared
        // at the top level of a file of the module set; checked by
        // CheckProperties once the module set is compiled. Null otherwise.
        private List<(string Name, string? Enum, string? File, string? Source, int Line, int Column)>? _propertyUses;

        // While Compile() runs: every bare read of a global (GetGlobal) and
        // index write into one (IndexSetGlobal) the compiler emitted (T3.8,
        // PROP-27; ClassBody: in a class body, where a field of the
        // instance is found first), every name it writes as a global
        // (SetGlobal), and whether a bytecode import (whose writes are not
        // known) joined the module set; checked by CheckGlobalReads once the
        // module set is compiled. Null / false otherwise.
        private List<(string Name, bool ClassBody, string? File, string? Source, int Line, int Column)>? _globalReads;
        private HashSet<string>? _globalWrites;
        private bool _globalWritesUnknown;

        // While Compile() runs: every call by name (Call, TailCall) the
        // compiler emitted (T3.8; ClassBody: in a class body, where the VM's
        // function-value fallback looks in the instance's fields first);
        // checked by CheckCalls once the module set is compiled. Null
        // otherwise.
        private List<(string Name, int Arguments, bool ClassBody, string? File, string? Source, int Line, int Column)>? _calls;

        // While Compile() runs: every bare assignment (a global, a local or a
        // class-body field) the compiler compiled that is not the declaration
        // of a const or enum (T3.8);
        // checked by CheckConstantWrites once the module set is compiled.
        // Null otherwise.
        private List<(string Name, string? File, string? Source, int Line, int Column)>? _constantWrites;

        // While Compile() runs: every operator or `::` check whose operand
        // reads a global or an `E :: x` of one (T3.8, PROP-28; the check
        // gets the type of such a read and returns the error message or
        // null), and the type of every `const`'s value; checked by
        // CheckTypedOperands once the module set is compiled. Null otherwise.
        private List<(System.Func<System.Func<string, Compiler.StaticType>, string?> Check, string? File, string? Source, int Line, int Column)>? _typedOperands;
        private Dictionary<string, System.Func<System.Func<string, Compiler.StaticType>, Compiler.StaticType>>? _constantTypes;

        // While Compile() runs: the errors found so far (T3.8), each once, in
        // the order found: those the compiler recovered from at a statement
        // or definition boundary, then those of the module-set checks
        // (CheckInstantiations to CheckConstantWrites); null otherwise. Every
        // one is reported.
        private List<Exception.SyntaxException>? _compileErrors;
        private Dictionary<string, HashSet<string>>? _enumMembers;
        private WarVM? _cachedVM;

        public readonly DefinitionScope GlobalDefinitionScope;
        public readonly MemoryScope GlobalMemoryScope;

        public MemoryScope UserMemoryScope => _memoryScope;

        /// <summary>
        /// Maximum number of bytecode instructions an outermost Run() or Call()
        /// invocation (or a coroutine resume by TickCoroutines) may execute,
        /// including everything nested inside it: constructors, imports, and
        /// callbacks a native makes into this instance (v2 plan T1.5, PROP-2).
        ///
        /// Set to 0 (default) to disable budgeting (unlimited execution).
        /// Recommended values: 100,000 for tick functions, 1,000,000 for Run().
        ///
        /// When the budget is exhausted, the VM raises "Instruction budget exceeded".
        /// The budget then stays exhausted until the next outermost entry: every
        /// further instruction raises it again, so no rescue or ensure body runs
        /// and the entry ends with the error (BUDGET-2), which halts the instance
        /// (plan T2.4). The host simulation continues.
        /// </summary>
        public int InstructionBudget { get; set; }

        // ── Source-map debugger ──

        /// <summary>
        /// Set this to receive callbacks when the VM hits a breakpoint or step
        /// point. Set to null to disable debugging (zero overhead).
        ///
        /// <code>
        ///   script.DebugHook = ctx => {
        ///       Console.WriteLine($"Paused at {ctx.FunctionName}:{ctx.Line}");
        ///       foreach (var local in ctx.Locals)
        ///           Console.WriteLine($"  {local.Key} = {local.Value}");
        ///       ctx.Action = StepMode.StepOver;
        ///   };
        /// </code>
        /// </summary>
        public DebugHook? DebugHook { get; set; }

        private readonly HashSet<int> _breakpoints = new();

        // ── Audit mode (v2 plan 8.1, T0.8; temporary) ──

        /// <summary>
        /// Receives an <see cref="Audit.AuditEvent"/> each time the VM takes a
        /// v1 code path whose behavior changes in WarScript v2 (mixed-type
        /// equality, text subtraction, ignored index writes, a function
        /// writing a global, ...). Null (the default) disables auditing; the
        /// cost is then one null check on those paths. Collect events with an
        /// <see cref="Audit.AuditReport"/>: <c>script.AuditSink = report.Add</c>.
        /// </summary>
        public Action<Audit.AuditEvent>? AuditSink { get; set; }

        /// <summary>Add a breakpoint at a source line number.</summary>
        public void AddBreakpoint(int line) => _breakpoints.Add(line);

        /// <summary>Remove a breakpoint at a source line number.</summary>
        public bool RemoveBreakpoint(int line) => _breakpoints.Remove(line);

        /// <summary>Remove all breakpoints.</summary>
        public void ClearBreakpoints() => _breakpoints.Clear();

        /// <summary>Get all active breakpoint line numbers.</summary>
        public IReadOnlyCollection<int> Breakpoints => _breakpoints;

        /// <summary>
        /// Internal: the VM reads this to check breakpoints.
        /// Returns true if the given line has a breakpoint set.
        /// </summary>
        internal bool HasBreakpoint(int line) => _breakpoints.Contains(line);

        /// <summary>
        /// Maximum bytes of heap allocation an outermost Run() or Call()
        /// invocation may perform, shared with everything nested inside it
        /// like <see cref="InstructionBudget"/>, before the VM raises an exception.
        ///
        /// Set to 0 (default) to disable memory budgeting (unlimited).
        /// Recommended values: 1_000_000 (1MB) for tick functions,
        /// 10_000_000 (10MB) for Run().
        ///
        /// Tracks allocations of strings, arrays, and class instances.
        /// Numeric and boolean values are stack-allocated and not counted.
        /// When the budget is exhausted, the VM raises "Memory budget exceeded"
        /// which can be caught by begin/rescue.
        /// </summary>
        public long MemoryBudget { get; set; }

        /// <summary>
        /// Longest text, in characters, that an operator may create
        /// (concatenation, repetition, index write). Creating a longer one
        /// raises "Text too long", which begin/rescue can catch; the check
        /// happens before anything is allocated (v2 plan 5.12, PROP-5, T1.4).
        /// </summary>
        public int MaxTextLength { get; set; } = 1_048_576;

        /// <summary>
        /// Most elements an array may grow to through an operator
        /// (<c>+</c>, <c>&lt;&lt;</c>, a literal) or <c>Array_insert</c>.
        /// Exceeding it raises "Array too long", which begin/rescue can
        /// catch; the check happens before anything is allocated (v2 plan
        /// 5.12, PROP-5, T1.4).
        /// </summary>
        public int MaxArrayLength { get; set; } = 1_048_576;

        // ── Formatting native objects (v2 plan T1.6, 5.10, PROP-14) ──

        /// <summary>
        /// The formatters registered with <see cref="RegisterNativeFormatter{T}"/>.
        /// </summary>
        internal readonly Native.NativeFormatters NativeFormatters = new();

        /// <summary>
        /// Registers how <c>print</c>, text concatenation and interpolation
        /// format native objects whose runtime type is exactly
        /// <typeparamref name="T"/>. Without one such an object prints as
        /// <c>&lt;TypeName&gt;</c>: the runtime never calls a host object's
        /// own <c>ToString()</c>, which may depend on the culture or go
        /// through <c>double</c> (DET-3). <c>F64Vec3</c> and function values
        /// have built-in formats that a registration does not change. The
        /// formatter must be deterministic; it is kept across
        /// <see cref="Reload"/>. It is host code, not a native function: a
        /// <see cref="Exception.ScriptErrorException"/> it throws raises a
        /// script error that <c>begin/rescue</c> can catch, any other
        /// exception is a host fault that stops the entry.
        /// </summary>
        public void RegisterNativeFormatter<T>(Func<T, string> formatter) =>
            NativeFormatters.Register(formatter);

        // ── Host entries and instance budgets (v2 plan T1.5, 6.5) ──

        /// <summary>
        /// Most host entries (<see cref="Run"/>, <see cref="Call"/>, a
        /// coroutine start or resume) that may be nested inside the outermost
        /// one: a native that calls back into the instance which calls the
        /// native again, and so on. A deeper entry does not run; it raises
        /// "Host nesting limit exceeded" (LimitExceeded) for the caller
        /// (PROP-5).
        /// </summary>
        public const int MaxHostNesting = 32;

        /// <summary>
        /// The instruction and memory budgets every VM of this instance
        /// charges. <see cref="InstructionBudget"/> and <see cref="MemoryBudget"/>
        /// are copied into it when an outermost entry starts (PROP-2).
        /// </summary>
        internal readonly ExecutionBudget Budget = new();

        /// <summary>Host entries currently executing; 0 when the instance is idle.</summary>
        internal int EntryDepth { get; private set; }

        /// <summary>
        /// Starts a host entry. The outermost one resets the budgets; one
        /// nested deeper than <see cref="MaxHostNesting"/> raises
        /// "Host nesting limit exceeded" instead and returns false, and the
        /// entry must then return without running anything (the error stays
        /// raised for the native that made the call). Pair a true result
        /// with <see cref="EndEntry"/> in a finally block.
        ///
        /// A nested entry also returns false, running nothing, while an
        /// earlier callback's error is still raised: that error belongs to
        /// the calling script (PROP-1), and the new entry's VM would otherwise
        /// take it for its own and could rescue it, letting the native
        /// suppress a callback error (6.5).
        /// </summary>
        internal bool BeginEntry()
        {
            if (EntryDepth == 0)
                _pendingHostFault = null;
            if (EntryDepth > 0 && ExceptionContext.IsRaised())
                return false;
            if (EntryDepth > MaxHostNesting)
            {
                ExceptionContext.RaiseException("Host nesting limit exceeded", ScriptErrorKind.LimitExceeded);
                return false;
            }
            if (EntryDepth == 0)
                Budget.Reset(InstructionBudget, MemoryBudget);
            EntryDepth++;
            return true;
        }

        /// <summary>
        /// Ends a host entry. An error the entry left unhandled is cleared at
        /// the outermost entry (its result carries it, and
        /// <see cref="FinishEntry"/> halts the instance with it); a nested
        /// entry leaves it raised, so it continues in the calling script from
        /// the native call site, where begin/rescue can catch it (PROP-1).
        /// </summary>
        internal void EndEntry()
        {
            EntryDepth--;
            if (EntryDepth == 0 && ExceptionContext.IsRaised())
                ExceptionContext.TakeUnhandled();
        }

        /// <summary>
        /// Completes an entry after <see cref="EndEntry"/> (or one refused
        /// before it began): at the outermost level an <c>Error</c> or
        /// <c>HostFault</c> result halts the instance (plan 6.4), and a host
        /// fault a nested entry recorded but that never reached this entry
        /// (a native that ignored the nested result) becomes the result.
        /// Nested results are returned unchanged.
        /// </summary>
        internal ScriptResult FinishEntry(ScriptResult result)
        {
            if (EntryDepth != 0) return result;
            if (_pendingHostFault != null && result.Status != ScriptStatus.HostFault)
                result = ReportHostFault(_pendingHostFault);
            _pendingHostFault = null;
            if (result.Status == ScriptStatus.Error || result.Status == ScriptStatus.HostFault)
                Halt(result.Error!);
            return result;
        }

        // ── Fault and halt model (plan 6.3, 6.4, T2.4) ──

        /// <summary>
        /// True once an unhandled error, a compile error or a host fault has
        /// ended an outermost entry. A halted instance answers every entry
        /// (<see cref="Run"/>, <see cref="Call"/>, <see cref="StartCoroutine(string, WarValue[], bool, out int)"/>,
        /// <see cref="TickCoroutines"/>) with <c>Halted</c> and the original
        /// <see cref="Fault"/>, running nothing, until <see cref="ClearFault"/>
        /// or <see cref="Reload"/>.
        /// </summary>
        public bool IsHalted { get; private set; }

        /// <summary>The error that halted the instance; null while it is not halted.</summary>
        public ScriptError? Fault { get; private set; }

        /// <summary>
        /// Invoked synchronously when the instance halts, after its coroutines
        /// were cleared and the error was logged. Entries made from a handler
        /// return <c>Halted</c>. An exception from a handler propagates to the
        /// host code that made the entry.
        /// </summary>
        public event Action<WarScriptLanguage, ScriptError>? Faulted;

        /// <summary>
        /// Whether the error that halts the instance is sent to
        /// <see cref="Logger"/> (as <c>[WarScript] </c> + <see cref="ScriptError.ToString"/>,
        /// plus the C# exception for a host fault). Default true (plan 6.8).
        /// </summary>
        public bool LogUnhandledErrors { get; set; } = true;

        /// <summary>
        /// When true, <see cref="Compile"/> fails on a warning (PROP-31, T3.8):
        /// every warning becomes an error diagnostic, nothing is installed,
        /// and the entry points return a <c>CompileError</c> result for the
        /// first one. Default false. Read when <see cref="Compile"/> runs:
        /// set it before the first compile (or call <see cref="Reload"/>
        /// afterwards), as the result is cached.
        /// </summary>
        public bool WarningsAsErrors { get; set; }

        /// <summary>
        /// A compile-time warning (T3.8: PROP-26, PROP-29) at
        /// <paramref name="line"/>, <paramref name="column"/> of
        /// <paramref name="scriptName"/> (an import path, or null for this
        /// script). Ignored outside <see cref="Compile"/>. Reported once:
        /// code the compiler emits twice (an ensure body inlined where a
        /// <c>break</c>/<c>next</c> leaves it, with the lambdas in it) warns
        /// once.
        /// </summary>
        internal void Warn(string message, string? scriptName, int line, int column)
        {
            if (_compileWarnings == null) return;
            var warning = new CompileDiagnostic(DiagnosticSeverity.Warning, message, scriptName ?? ScriptName, line, column);
            foreach (var w in _compileWarnings)
                if (w.Message == warning.Message && w.ScriptName == warning.ScriptName
                    && w.Line == warning.Line && w.Column == warning.Column)
                    return;
            _compileWarnings.Add(warning);
        }

        /// <summary>
        /// Returns a halted instance to normal operation. State changed
        /// before the fault (globals, native side effects) is kept; the
        /// coroutines cleared by the fault stay cleared.
        /// </summary>
        public void ClearFault()
        {
            IsHalted = false;
            Fault = null;
        }

        /// <summary>
        /// A host fault a nested entry caught (plan 6.3): the entries around
        /// it must stop too, so the VM that called the native rethrows it
        /// (<see cref="ThrowPendingHostFault"/>) and the outermost entry
        /// reports it and halts.
        /// </summary>
        private System.Exception? _pendingHostFault;

        /// <summary>
        /// Called by the VM after a native returned or threw: when an entry
        /// nested inside that native ended with a host fault, rethrow it so
        /// the calling VM stops as well (it treats it as its own host fault).
        /// </summary>
        internal void ThrowPendingHostFault()
        {
            if (_pendingHostFault != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_pendingHostFault).Throw();
        }

        internal ScriptResult HaltedResult() => ScriptResult.HaltedBy(Fault!);

        /// <summary>The steps of plan 6.4 after the VM unwound and the contexts were restored.</summary>
        private void Halt(ScriptError error)
        {
            ClearCoroutines();
            IsHalted = true;
            Fault = error;
            if (LogUnhandledErrors && Logger != null)
                Log(error.HostException == null
                    ? "[WarScript] " + error
                    : "[WarScript] " + error + "\n" + error.HostException);
            Faulted?.Invoke(this, error);
        }

        /// <summary>
        /// String interner for reducing GC pressure. Short strings created by
        /// the VM (concatenation results, ToString() conversions) are deduplicated
        /// through this cache. Pre-allocated integer strings (0–999) are returned
        /// without any hashing. Shared across all VMs and coroutines.
        /// </summary>
        internal readonly StringInterner Strings = new();

        // ── Coroutine support (BytecodeCoroutine: VM suspend/resume) ──
        private readonly List<ICoroutine> _coroutines = new();
        private int _nextCoroutineId = 1;

        public bool IsYielded { get; private set; }
        public YieldType YieldedType { get; private set; }
        public F64 YieldedWaitDuration { get; private set; }

        public WarScriptLanguage(
            string scriptName,
            string sourceCode,
            Func<string, string?>? fileResolver,
            Action<WarScriptLanguage, string>? logger,
            Func<string, byte[]?>? bytecodeResolver = null)
        {
            ScriptName = scriptName;
            FileResolver = fileResolver;
            BytecodeResolver = bytecodeResolver;
            Logger = logger;

            NullExpr = new ConstantExpression(WarValue.Null);
            TrueExpr = new ConstantExpression(WarValue.True);
            FalseExpr = new ConstantExpression(WarValue.False);
            ThisExpr = new ThisExpression(this);

            DefinitionContext = new DefinitionContext(this);
            ExceptionContext = new ExceptionContext(this);
            DefaultStep = new ConstantExpression(WarValue.FromNumeric(1));

            _sourceCode = sourceCode;

            var nativeDefinitionScope = DefinitionContext.NewScope();
            // The host globals and the script's globals (parent: the host
            // globals). No frame pushes a MemoryScope since T3.5, so there is
            // no scope stack: the VM finds them through WarVM.NameScope.
            var nativeMemoryScope = new MemoryScope(this, null, MemoryScopeKind.Native);

            DefinitionContext.PushScope(nativeDefinitionScope);

            GlobalDefinitionScope = nativeDefinitionScope;
            GlobalMemoryScope = nativeMemoryScope;

            _definitionScope = DefinitionContext.NewScope();
            _memoryScope = new MemoryScope(this, nativeMemoryScope, MemoryScopeKind.User);

            DefinitionContext.EndScope();
        }

        // ────────────────────────────────────────────────────────
        //  Compile — lex, parse and compile once (plan T2.2)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Lex, parse and compile the script to bytecode without running any
        /// of it. Idempotent: the first call does the work and every later call
        /// returns the same result, success or failure, until
        /// <see cref="Reload"/> replaces the source (after
        /// <see cref="LoadBytecode"/> the script is compiled). A script error
        /// is reported as an error diagnostic, not thrown; when the script does
        /// not compile, the instance holds none of its definitions.
        /// <see cref="Run"/>, <see cref="Call"/> and <see cref="StartCoroutine"/>
        /// call it when needed. Register natives, <c>[WsConst]</c> and
        /// <c>[WsEnum]</c> values first: the parser checks assignments against
        /// the constants known when it runs. Not a host entry: it runs no
        /// script code and uses no budget. Imports are resolved here (T3.7,
        /// 5.8, DEC-7): each imported file, through <see cref="BytecodeResolver"/>
        /// and then <see cref="FileResolver"/>, is compiled before the file
        /// importing it, so an import cycle, a file that cannot be found or
        /// read and an error in an imported file are compile errors. The
        /// imported files' functions and classes are the script's once it
        /// compiles (one namespace); <see cref="Run"/> runs their top-level
        /// code, each file's once per instance, before the script's.
        /// </summary>
        public CompileResult Compile()
        {
            if (_compileResult != null) return _compileResult;

            // The parser adds `const`/`enum` names as it goes; a failed
            // compile puts the set back, so none of the script stays behind.
            var constantsBefore = new HashSet<string>(ConstantNames);
            var warnings = new List<CompileDiagnostic>();
            _compileWarnings = warnings;
            _instantiations = new List<(string, int, bool, string?, string?, int, int)>();
            _propertyUses = new List<(string, string?, string?, string?, int, int)>();
            _enumMembers = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
            _globalReads = new List<(string, bool, string?, string?, int, int)>();
            _globalWrites = new HashSet<string>(System.StringComparer.Ordinal);
            _globalWritesUnknown = false;
            _calls = new List<(string, int, bool, string?, string?, int, int)>();
            _constantWrites = new List<(string, string?, string?, int, int)>();
            _typedOperands = new List<(System.Func<System.Func<string, Compiler.StaticType>, string?>, string?, string?, int, int)>();
            _constantTypes = new Dictionary<string, System.Func<System.Func<string, Compiler.StaticType>, Compiler.StaticType>>(System.StringComparer.Ordinal);
            _compileErrors = new List<Exception.SyntaxException>();
            DefinitionContext.PushScope(_definitionScope);
            try
            {
                var tokens = LexicalParser.Parse(_sourceCode);
                _parsingImports = new List<DefinitionScope>();
                // One namespace for the module set (5.8, PROP-12): every
                // file's top-level definitions go into the same record.
                _definitionScope.Declared = new Context.Definition.DeclaredDefinitions();
                var imports = new List<string>();
                ResolveImports(tokens, ScriptName, _sourceCode, _definitionScope, new List<string>(), imports);
                ParsingFile = ScriptName;
                ParsingSource = _sourceCode;
                var module = StatementParser.ParseModule(this, tokens, ScriptName);
                _cachedCompiled = Compiler.CompileScript(this, module.TopLevel, _definitionScope);
                CheckInstantiations();
                CheckProperties();
                CheckGlobalReads();
                CheckCalls();
                CheckConstantWrites();
                CheckTypedOperands();

                // Every error the compiler recovered from and the module-set
                // checks found (T3.8), the first being the one the entry
                // points report, then the warnings.
                if (_compileErrors.Count > 0)
                {
                    RollBackCompile(constantsBefore);
                    _compileResult = FailedCompile(_compileErrors, warnings);
                    return _compileResult;
                }

                // Warnings as errors (PROP-31): the script does not compile,
                // and every warning is reported as an error.
                if (WarningsAsErrors && warnings.Count > 0)
                {
                    RollBackCompile(constantsBefore);
                    var first = warnings[0];
                    var error = new Exception.CompileError(first.Message, first.ScriptName, first.Line, first.Column);
                    if (first.ScriptName == ScriptName) error.LocateIn(ScriptName, _sourceCode);
                    var errors = new CompileDiagnostic[warnings.Count];
                    for (int i = 0; i < errors.Length; i++)
                    {
                        var w = warnings[i];
                        errors[i] = new CompileDiagnostic(DiagnosticSeverity.Error, w.Message, w.ScriptName, w.Line, w.Column);
                    }
                    _compileError = error;
                    _compileResult = new CompileResult(false, errors);
                    return _compileResult;
                }

                // One namespace for the module set (5.8): every import's
                // definitions are the script's from now on, before any
                // top-level code runs, so the host can call them as soon as
                // the script compiles. Run only runs the imports' top-level
                // code.
                foreach (var imported in _parsingImports)
                    imported.CopyImportedDefinitionsTo(_definitionScope);

                // Discard the AST — bytecode is the source of truth.
                // This frees all Statement/Expression nodes for GC while
                // keeping the DefinitionScope tree (function names, class
                // details, compiled bytecode) intact.
                DiscardAST(_definitionScope);
                _imports = imports;
                _compileResult = warnings.Count == 0 ? CompileResult.Succeeded : new CompileResult(true, warnings.ToArray());
            }
            catch (Exception.SyntaxException e)
            {
                // Whatever the parser had added stays out of reach: a
                // definition without bytecode cannot be called.
                RollBackCompile(constantsBefore);
                // The errors the compiler recovered from before it, if any,
                // then this one (the first is the one the entry points
                // report), then the warnings found before it.
                var errors = _compileErrors ?? new List<Exception.SyntaxException>();
                if (!errors.Contains(e)) errors.Add(e);
                _compileResult = FailedCompile(errors, warnings);
            }
            catch
            {
                // A compiler bug (not cached): roll back too, so that the next
                // Compile() parses from a clean instance instead of reporting
                // its own consts as reassigned.
                RollBackCompile(constantsBefore);
                throw;
            }
            finally
            {
                _compileWarnings = null;
                _instantiations = null;
                _propertyUses = null;
                _enumMembers = null;
                _globalReads = null;
                _globalWrites = null;
                _globalWritesUnknown = false;
                _calls = null;
                _constantWrites = null;
                _typedOperands = null;
                _constantTypes = null;
                _compileErrors = null;
                _parsingImports = null;
                ParsingFile = null;
                ParsingSource = null;
                _definitionScope.Declared = null;
                DefinitionContext.EndScope();
            }
            return _compileResult;
        }

        /// <summary>
        /// Resolves and compiles the imports at the top of a file (T3.7, 5.8),
        /// in textual order, each one's own imports first. The file is
        /// <paramref name="importer"/> (its name, for errors) with
        /// <paramref name="importerSource"/> and <paramref name="tokens"/>;
        /// its definitions are <paramref name="importerScope"/>, the parent
        /// of each import's definitions (as when the import ran, until T3.7
        /// gives the module set one namespace). <paramref name="chain"/> is
        /// the paths being resolved, outermost first, for the cycle check.
        /// The parser checks the imports' placement later; this reads only
        /// the leading <c>import "path"</c> statements. Each path is added
        /// to <paramref name="paths"/> (when given) once, in order: the
        /// file's import list (the module header, T3.7).
        /// </summary>
        private void ResolveImports(IReadOnlyList<Token.Token> tokens, string? importer, string importerSource,
            DefinitionScope importerScope, List<string> chain, List<string>? paths = null)
        {
            int i = 0;
            while (NextSignificant(tokens, ref i, out var keyword)
                   && keyword.Type == Token.TokenType.Keyword && keyword.Value == "import"
                   && NextSignificant(tokens, ref i, out var path) && path.Type == Token.TokenType.Text)
            {
                ResolveImport(path.Value, keyword.RowNumber, keyword.Column, importer, importerSource, importerScope, chain);
                if (paths != null && !paths.Contains(path.Value)) paths.Add(path.Value);
            }

            static bool NextSignificant(IReadOnlyList<Token.Token> list, ref int index, out Token.Token token)
            {
                while (index < list.Count)
                {
                    token = list[index++];
                    if (token.Type != Token.TokenType.Comment && token.Type != Token.TokenType.LineBreak
                        && token.Type != Token.TokenType.Whitespace)
                        return true;
                }
                token = default;
                return false;
            }
        }

        /// <summary>
        /// Resolves one import of <paramref name="importer"/> (its source
        /// <paramref name="importerSource"/>, null for a loaded module, whose
        /// errors have no position: <paramref name="line"/> 0).
        /// </summary>
        private void ResolveImport(string path, int line, int column, string? importer, string? importerSource,
            DefinitionScope importerScope, List<string> chain)
        {
            Exception.SyntaxException Error(string message) =>
                new Exception.SyntaxException(message, line, column).LocateIn(importer, importerSource);

            var start = chain.IndexOf(path);
            if (start >= 0)
                throw Error("Circular import: " + string.Join(" -> ", chain.GetRange(start, chain.Count - start)) + " -> " + path);
            if (ResolvedImports.ContainsKey(path)) return;

            // Precompiled first, as when the import ran. Its own imports are
            // in its module header (format 10, T3.7) and are resolved first,
            // as a source file's are.
            var precompiled = LoadImportedBytecode(path, importerScope, out var loadedScope, out var loadedImports);
            if (precompiled != null)
            {
                loadedScope!.Declared = importerScope.Declared;
                chain.Add(path);
                foreach (var nested in loadedImports)
                    ResolveImport(nested, 0, 0, path, null, loadedScope, chain);
                chain.RemoveAt(chain.Count - 1);

                // Its definitions join the namespace (PROP-12); a clash is
                // reported here, as the file has no source to point into.
                var declared = importerScope.Declared;
                if (declared != null)
                {
                    foreach (var c in loadedScope!.ClassDefinitions)
                        if (declared.AddClass(c.ClassDetails.Name, path, 0) is { } clash)
                            throw Error($"Cannot import '{path}': {clash}");
                    foreach (var f in loadedScope.Functions)
                        if (declared.AddFunction(f.Details.Name, f.Details.MinArity, f.Details.Arguments.Count, path, 0) is { } clash)
                            throw Error($"Cannot import '{path}': {clash}");
                }
                AddResolvedImport(new ImportedModule(path, precompiled, loadedScope!));
                _parsingImports?.Add(loadedScope!);
                // Its top level and functions may write any global (PROP-27).
                _globalWritesUnknown = true;
                return;
            }

            if (FileResolver == null) throw Error($"Cannot import '{path}': no file resolver");
            string? source;
            try { source = FileResolver(path); }
            catch (System.Exception e) { throw Error($"Failed to read import '{path}': {e.Message}"); }
            if (source == null) throw Error($"Import '{path}' not found");

            var scope = new DefinitionScope(this, importerScope) { Declared = importerScope.Declared };
            chain.Add(path);
            try
            {
                var tokens = LexicalParser.Parse(source);
                ResolveImports(tokens, path, source, scope, chain);
                DefinitionContext.PushScope(scope);
                ParsingFile = path;
                ParsingSource = source;
                try
                {
                    var module = StatementParser.ParseModule(this, tokens, path);
                    AddResolvedImport(new ImportedModule(path, Compiler.CompileScript(this, module.TopLevel, scope), scope));
                }
                finally
                {
                    DefinitionContext.EndScope();
                }
            }
            catch (Exception.SyntaxException e)
            {
                // Its position is in the imported file (T3.1).
                e.LocateIn(path, source);
                throw;
            }
            chain.RemoveAt(chain.Count - 1);
            _parsingImports?.Add(scope);
        }

        /// <summary>
        /// Resolves and compiles the imports of a module
        /// <see cref="LoadBytecode"/> read (T3.7), as <see cref="Compile"/>
        /// does for source: <paramref name="imports"/> in order, each one's
        /// own imports first, under <paramref name="scope"/>, the module's
        /// definitions, which with every import share one namespace (5.8,
        /// PROP-12). Fills <see cref="ResolvedImports"/> and returns the
        /// definitions of every import resolved, in the order resolved.
        /// Throws the compile error of the first that fails.
        /// </summary>
        private List<DefinitionScope> ResolveLoadedImports(IReadOnlyList<string> imports, DefinitionScope scope)
        {
            var resolved = new List<DefinitionScope>();
            if (imports.Count == 0) return resolved;

            var declared = new Context.Definition.DeclaredDefinitions();
            foreach (var c in scope.ClassDefinitions)
                declared.AddClass(c.ClassDetails.Name, ScriptName, 0);
            foreach (var f in scope.Functions)
                declared.AddFunction(f.Details.Name, f.Details.MinArity, f.Details.Arguments.Count, ScriptName, 0);

            _parsingImports = resolved;
            scope.Declared = declared;
            DefinitionContext.PushScope(scope);
            try
            {
                var chain = new List<string>();
                foreach (var path in imports)
                    ResolveImport(path, 0, 0, ScriptName, null, scope, chain);
                return resolved;
            }
            finally
            {
                _parsingImports = null;
                ParsingFile = null;
                ParsingSource = null;
                scope.Declared = null;
                DefinitionContext.EndScope();
            }
        }

        /// <summary>
        /// For a named-argument call the scope chain does not resolve while
        /// <see cref="Compile"/> parses: the function of that name and arity
        /// in the import resolved last that has one, or null.
        /// </summary>
        internal Context.Definition.FunctionDefinition? FindImportedFunction(string name, int argumentCount)
        {
            if (_parsingImports == null) return null;
            for (int i = _parsingImports.Count - 1; i >= 0; i--)
            {
                var found = _parsingImports[i].GetFunction(name, argumentCount);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// For a base class the scope chain does not resolve while
        /// <see cref="Compile"/> compiles (T3.7): the class of that name in
        /// the imports resolved so far, or null. A class is defined once in
        /// the module set (PROP-12), so there is at most one.
        /// </summary>
        internal Context.Definition.ClassDefinition? FindImportedClass(string name)
        {
            if (_parsingImports == null) return null;
            for (int i = _parsingImports.Count - 1; i >= 0; i--)
            {
                var found = _parsingImports[i].GetOwnClass(name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Expression spans recorded while parsing (T3.2): null, except
        /// during <see cref="ParseForMigration"/>, so a normal parse records
        /// nothing. The migration tool supplies a reference-equality map.
        /// </summary>
        internal Dictionary<Expression.IExpression, Expression.ExpressionSpan>? ExpressionSpans;

        /// <summary>
        /// The kind of body the parser is in (T3.3): set and restored by
        /// <see cref="StatementParser"/>, read by it and by the expression
        /// reader. Parse-time state only.
        /// </summary>
        internal ParseBody ParsingBody;

        /// <summary>
        /// How many class definitions the parser is inside (T3.8, PROP-29):
        /// <c>this</c> and <c>@x</c> outside every class are a compile
        /// error. Parse-time state only.
        /// </summary>
        internal int ParsingClassDepth;

        /// <summary>
        /// The file <see cref="Compile"/> is parsing (the script's name or an
        /// import's path), named by a duplicate-definition error about a
        /// definition in another file (PROP-12). Null otherwise.
        /// </summary>
        internal string? ParsingFile;

        /// <summary>The source text of <see cref="ParsingFile"/>, for the position of a deferred compile error. Null otherwise.</summary>
        internal string? ParsingSource;

        /// <summary>
        /// Records a <c>new <paramref name="className"/></c> with
        /// <paramref name="arguments"/> arguments (<paramref name="nested"/>:
        /// <c>obj :: new C [...]</c>; -1: the class of an <c>is</c> or
        /// <c>as</c>, which only has to exist) at <paramref name="line"/>,
        /// <paramref name="column"/> of <paramref name="file"/> (T3.8,
        /// PROP-29), checked by <see cref="CheckInstantiations"/> once every
        /// class of the module set is known. Ignored outside
        /// <see cref="Compile"/>.
        /// </summary>
        internal void CheckInstantiation(string className, int arguments, bool nested, string? file, int line, int column) =>
            _instantiations?.Add((className, arguments, nested, file, ParsingSource, line, column));

        /// <summary>
        /// The compile errors for every recorded <c>new C [...]</c> (T3.8,
        /// PROP-29) whose class is no class of the module set or the host
        /// (<c>Class 'C' is not defined</c>, as the VM would raise), or that
        /// takes another number of arguments than every class named
        /// <c>C</c> (<c>Class 'C' takes 2 arguments, not 3</c>). A class may
        /// be defined in a file compiled after the <c>new</c> (an import can
        /// instantiate a class of its importer), hence the deferral. Classes
        /// are those of the top level of each file, nested in them, and in
        /// the host's definition scopes; a class defined inside a function or
        /// a block is never found by <c>new</c> at runtime, so it does not
        /// count. A nested <c>obj :: new C [...]</c> naming no such class is
        /// left to the VM, which reports a foreign <c>obj</c> as such (6.7).
        /// The class of an <c>is</c> or <c>as</c> must exist too, at any
        /// arity (T3.8), though the VM would only answer false or null: like
        /// a <c>::</c> name, a class of another script instance's value
        /// (6.7) must be known to the script that tests for it.
        /// </summary>
        private void CheckInstantiations()
        {
            if (_instantiations == null || _instantiations.Count == 0) return;
            var arities = new Dictionary<string, SortedSet<int>>(System.StringComparer.Ordinal);
            foreach (var c in ModuleSetClasses())
            {
                if (!arities.TryGetValue(c.ClassDetails.Name, out var set))
                    arities[c.ClassDetails.Name] = set = new SortedSet<int>();
                set.Add(c.ClassDetails.Properties.Count);
            }

            foreach (var (name, arguments, nested, file, source, line, column) in _instantiations)
            {
                string message;
                if (!arities.TryGetValue(name, out var set))
                {
                    if (nested) continue;
                    message = $"Class '{name}' is not defined";
                }
                else if (arguments >= 0 && !set.Contains(arguments))
                    message = ConstructorArityMessage(name, ArityList(set), arguments);
                else continue;
                ReportCompileError(new Exception.CompileError(message, file, line, column).LocateIn(file, source));
            }
        }

        /// <summary>
        /// Every class <c>new</c> can instantiate while <see cref="Compile"/>
        /// runs (T3.8): those at the top level of each file of the module
        /// set, the classes nested in them, and the classes of the host's
        /// definition scopes. A class defined inside a function or a block is
        /// never found by <c>new</c> at runtime, so it is not one.
        /// </summary>
        private List<ClassDefinition> ModuleSetClasses()
        {
            var classes = new List<ClassDefinition>();
            void Add(DefinitionScope scope)
            {
                foreach (var c in scope.ClassDefinitions)
                {
                    classes.Add(c);
                    Add(c.GetDefinitionScope());
                }
            }
            Add(_definitionScope);
            if (_parsingImports != null)
                foreach (var imported in _parsingImports) Add(imported);
            for (var host = _definitionScope.Parent; host != null; host = host.Parent) Add(host);
            return classes;
        }

        /// <summary>
        /// Records the members of an enum declared at the top level of a file
        /// of the module set (T3.8, PROP-29), with <c>values</c>,
        /// <c>names</c>, <c>count</c> and <c>name</c>, for
        /// <see cref="CheckProperties"/>.
        /// Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void DeclareEnum(string name, IEnumerable<string> members)
        {
            if (_enumMembers == null) return;
            var set = new HashSet<string>(members, System.StringComparer.Ordinal) { "values", "names", "count", "name" };
            _enumMembers[name] = set;
        }

        /// <summary>
        /// Records <c>obj :: <paramref name="name"/></c> (a read, a write or
        /// a method call) at <paramref name="line"/>, <paramref name="column"/>
        /// of <paramref name="file"/> (T3.8, PROP-29);
        /// <paramref name="enumName"/> is <c>obj</c> when it is a bare name
        /// read as a global. Checked by <see cref="CheckProperties"/> once
        /// every class of the module set is known. Ignored outside
        /// <see cref="Compile"/>.
        /// </summary>
        internal void CheckProperty(string name, string? enumName, string? file, int line, int column) =>
            _propertyUses?.Add((name, enumName, file, ParsingSource, line, column));

        /// <summary>
        /// The compile errors for every recorded <c>::</c> (T3.8, PROP-29)
        /// that can only fail at runtime:
        /// <list type="bullet">
        /// <item><c>E :: X</c> or <c>E :: X [...]</c> where <c>E</c> is an
        /// enum declared at the top level of a file of the module set (a
        /// constant, so the global is that enum) and <c>X</c> is none of its
        /// members, <c>values</c>, <c>names</c>, <c>count</c> or
        /// <c>name</c>: <c>'X' is not a member of enum 'E'</c>;</item>
        /// <item><c>obj :: x</c> or <c>obj :: x [...]</c> where <c>x</c> is
        /// neither a field nor a method of any class of the module set or the
        /// host: <c>'x' is not a field or method of any class</c>. Not
        /// checked when a host class's fields are unknown (it was registered
        /// without them, so the VM accepts any name), except a host enum
        /// (<c>[WsEnum]</c>): a host constant named like its class, whose
        /// fields are what that instance holds.</item>
        /// </list>
        /// <c>this :: x</c> and <c>@x</c> are checked against the class
        /// itself by the compiler; a nested <c>obj :: new C</c> by
        /// <see cref="CheckInstantiations"/>.
        /// </summary>
        private void CheckProperties()
        {
            if (_propertyUses == null || _propertyUses.Count == 0) return;
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var unknownFields = new HashSet<ClassDefinition>();
            foreach (var c in ModuleSetClasses())
            {
                foreach (var f in c.GetDefinitionScope().Functions) names.Add(f.Details.Name);
                names.UnionWith(c.ClassDetails.Properties);
                if (c.Fields != null) names.UnionWith(c.Fields);
                else unknownFields.Add(c);
            }
            // A host enum: its instance is a host constant.
            if (unknownFields.Count > 0)
                foreach (var global in GlobalMemoryScope.GetAllLocals())
                    if (global.Value.IsClass && ConstantNames.Contains(global.Key)
                        && global.Value.ClassValue.Definition.ClassDetails.Name == global.Key
                        && unknownFields.Remove(global.Value.ClassValue.Definition))
                        names.UnionWith(global.Value.ClassValue.MemoryScope.GetAllLocals().Keys);
            var namesKnown = unknownFields.Count == 0;

            foreach (var (name, enumName, file, source, line, column) in _propertyUses)
            {
                string? message = null;
                if (enumName != null && _enumMembers != null && ConstantNames.Contains(enumName)
                    && _enumMembers.TryGetValue(enumName, out var members))
                {
                    if (!members.Contains(name)) message = $"'{name}' is not a member of enum '{enumName}'";
                }
                else if (namesKnown && !names.Contains(name))
                    message = $"'{name}' is not a field or method of any class";
                if (message != null)
                    ReportCompileError(new Exception.CompileError(message, file, line, column).LocateIn(file, source));
            }
        }

        /// <summary>
        /// Records a bare read of the global <paramref name="name"/> (or an
        /// index write into it, which reads it too) at <paramref name="line"/>,
        /// <paramref name="column"/> of <paramref name="file"/> (T3.8,
        /// PROP-27; <paramref name="classBody"/>: in a class body, where the
        /// VM looks in the instance's fields first), checked by
        /// <see cref="CheckGlobalReads"/> once the module set is compiled.
        /// Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void CheckGlobalRead(string name, bool classBody, string? file, int line, int column) =>
            _globalReads?.Add((name, classBody, file, ParsingSource, line, column));

        /// <summary>
        /// Records that the module set writes the global <paramref name="name"/>
        /// (a <c>SetGlobal</c>: a top-level assignment, a <c>const</c>, an
        /// <c>enum</c>, an assignment of a name declared <c>global</c>), for
        /// <see cref="CheckGlobalReads"/>. Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void DeclareGlobalWrite(string name) => _globalWrites?.Add(name);

        /// <summary>
        /// The compile error <c>undefined variable 'x'</c> for every
        /// recorded global read (T3.8, PROP-27, DEC-21) of a name that no file
        /// of the module set ever writes as a global and that is not defined
        /// when <see cref="Compile"/> runs (a host global, or a global the
        /// instance already holds, as after a <see cref="Reload"/>): such a
        /// read can only raise NameError at runtime. In a class body a name
        /// that is a field of any class of the module set counts as written
        /// (the instance's one field scope, Q-16, may hold a parameter of a
        /// derived class). Nothing is checked when a bytecode import is in
        /// the module set (which globals it writes is not known). A name
        /// written only on a path that does not run still raises NameError at
        /// runtime.
        /// </summary>
        private void CheckGlobalReads()
        {
            if (_globalReads == null || _globalReads.Count == 0 || _globalWritesUnknown) return;
            HashSet<string>? fields = null;
            foreach (var (name, classBody, file, source, line, column) in _globalReads)
            {
                if (_globalWrites!.Contains(name) || UserMemoryScope.TryGet(name, out _)) continue;
                if (classBody)
                {
                    if (fields == null)
                    {
                        fields = new HashSet<string>(System.StringComparer.Ordinal);
                        foreach (var c in ModuleSetClasses())
                        {
                            fields.UnionWith(c.ClassDetails.Properties);
                            if (c.Fields != null) fields.UnionWith(c.Fields);
                        }
                    }
                    if (fields.Contains(name)) continue;
                }
                ReportCompileError(new Exception.CompileError(Bytecode.WarVM.UndefinedVariableMessage(name), file, line, column)
                    .LocateIn(file, source));
            }
        }

        /// <summary>
        /// Records a call by name of <paramref name="name"/> with
        /// <paramref name="arguments"/> arguments at <paramref name="line"/>,
        /// <paramref name="column"/> of <paramref name="file"/> (T3.8;
        /// <paramref name="classBody"/>: in a class body), checked by
        /// <see cref="CheckCalls"/> once the module set is compiled. Ignored
        /// outside <see cref="Compile"/>.
        /// </summary>
        internal void CheckCall(string name, int arguments, bool classBody, string? file, int line, int column) =>
            _calls?.Add((name, arguments, classBody, file, ParsingSource, line, column));

        /// <summary>
        /// The compile errors for every recorded call by name (T3.8,
        /// DEC-21) that can only raise NameError at runtime: no function of
        /// that name (<c>Function 'f' is not defined</c>) or none taking that
        /// many arguments (<c>Function 'f' takes 1 or 2 arguments, not 3</c>)
        /// anywhere a call can find one — the top level of each file of the
        /// module set, the methods of its classes (a function called from a
        /// method finds the method's class), the host's scopes — and no
        /// global of that name that could hold a function value (the VM's
        /// fallback): none the module set writes, none defined when
        /// <see cref="Compile"/> runs and, in a class body, no field of a
        /// class of the module set. A function defined inside a function or
        /// a block is never found by a call at runtime, so it does not count.
        /// Nothing is checked when a bytecode import is in the module set
        /// (which globals it writes is not known).
        /// </summary>
        private void CheckCalls()
        {
            if (_calls == null || _calls.Count == 0 || _globalWritesUnknown) return;
            var arities = new Dictionary<string, SortedSet<int>>(System.StringComparer.Ordinal);
            void Add(DefinitionScope scope)
            {
                foreach (var f in scope.Functions)
                {
                    if (!arities.TryGetValue(f.Details.Name, out var set))
                        arities[f.Details.Name] = set = new SortedSet<int>();
                    for (var a = f.Details.MinArity; a <= f.Details.Arguments.Count; a++) set.Add(a);
                }
            }
            Add(_definitionScope);
            if (_parsingImports != null)
                foreach (var imported in _parsingImports) Add(imported);
            for (var host = _definitionScope.Parent; host != null; host = host.Parent) Add(host);
            foreach (var c in ModuleSetClasses()) Add(c.GetDefinitionScope());

            HashSet<string>? fields = null;

            foreach (var (name, arguments, classBody, file, source, line, column) in _calls)
            {
                arities.TryGetValue(name, out var set);
                if (set != null && set.Contains(arguments)) continue;
                if (_globalWrites!.Contains(name) || UserMemoryScope.TryGet(name, out _)) continue;
                if (classBody)
                {
                    if (fields == null)
                    {
                        fields = new HashSet<string>(System.StringComparer.Ordinal);
                        foreach (var c in ModuleSetClasses())
                        {
                            fields.UnionWith(c.ClassDetails.Properties);
                            if (c.Fields != null) fields.UnionWith(c.Fields);
                        }
                    }
                    if (fields.Contains(name)) continue;
                }
                var message = set == null
                    ? $"Function '{name}' is not defined"
                    : $"Function '{name}' takes {ArityList(set)}, not {arguments.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                ReportCompileError(new Exception.CompileError(message, file, line, column).LocateIn(file, source));
            }
        }

        /// <summary>
        /// Records a bare assignment of <paramref name="name"/> (to a global,
        /// a local or a class-body field; not the declaration of a
        /// <c>const</c> or <c>enum</c>) at <paramref name="line"/>, <paramref name="column"/>
        /// of <paramref name="file"/> (T3.8), checked by
        /// <see cref="CheckConstantWrites"/> once the module set is compiled.
        /// Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void CheckConstantWrite(string name, string? file, int line, int column) =>
            _constantWrites?.Add((name, file, ParsingSource, line, column));

        /// <summary>
        /// The compile error <c>Cannot reassign constant 'X'</c> for every
        /// recorded bare assignment (T3.8) of a name that is a constant once the
        /// module set is parsed: a <c>const</c> or <c>enum</c> of any of its
        /// files, or a host constant. The parser rejects a reassignment it
        /// reads after the declaration; this check covers the writes it reads
        /// first: in an import of the file declaring the constant, or earlier
        /// in the same file (a function or class above it, assigning it as a
        /// global, a local or a field; a top-level assignment before it). A
        /// bytecode import's writes are not known.
        /// </summary>
        private void CheckConstantWrites()
        {
            if (_constantWrites == null) return;
            foreach (var (name, file, source, line, column) in _constantWrites)
                if (ConstantNames.Contains(name))
                    ReportCompileError(new Exception.CompileError($"Cannot reassign constant '{name}'", file, line, column)
                        .LocateIn(file, source));
        }

        /// <summary>True while <see cref="Compile"/> runs: the compiler records the checks of <see cref="CheckTypedOperands"/>.</summary>
        internal bool CollectsTypedOperands => _typedOperands != null;

        /// <summary>
        /// Records a check of an operator or <c>::</c> at
        /// <paramref name="line"/>, <paramref name="column"/> of
        /// <paramref name="file"/> whose operand reads a global, or an
        /// <c>E :: x</c> of one (T3.8, PROP-28), for
        /// <see cref="CheckTypedOperands"/>. Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void CheckTypedOperand(System.Func<System.Func<string, Compiler.StaticType>, string?> check, string? file, int line, int column) =>
            _typedOperands?.Add((check, file, ParsingSource, line, column));

        /// <summary>
        /// Records the type of the value a <c>const</c> declaration writes
        /// to the global <paramref name="name"/> (given the types of the
        /// constants and enum members it reads), for
        /// <see cref="CheckTypedOperands"/>. Ignored outside <see cref="Compile"/>.
        /// </summary>
        internal void DeclareConstantType(string name, System.Func<System.Func<string, Compiler.StaticType>, Compiler.StaticType> type)
        {
            if (_constantTypes != null) _constantTypes[name] = type;
        }

        /// <summary>
        /// PROP-28 for constants and enum members (T3.8): every recorded
        /// operator or <c>::</c> check is run again with the type of each
        /// global it reads that is a <c>const</c> of the module set (the type
        /// of its declared value) or of each <c>E :: x</c> where <c>E</c> is
        /// an enum declared at a file's top level (a member or <c>count</c>:
        /// a number; <c>values</c>, <c>names</c>: an array;
        /// <c>E :: name [...]</c>: a text), and its error is reported at the
        /// operator. Such a global holds nothing else whenever it is read
        /// without an error: no other write compiles (<see cref="CheckConstantWrites"/>),
        /// and a read before the declaration runs is a NameError. So a name
        /// the instance or the host already holds when <see cref="Compile"/>
        /// runs is not typed, and nothing is checked when a bytecode import
        /// (whose writes are unknown) is in the module set. Host constants
        /// are not typed: a manifest's stub holds null.
        /// </summary>
        private void CheckTypedOperands()
        {
            if (_typedOperands == null || _typedOperands.Count == 0 || _globalWritesUnknown) return;
            var known = new Dictionary<string, Compiler.StaticType>(System.StringComparer.Ordinal);
            var visiting = new HashSet<string>(System.StringComparer.Ordinal);

            Compiler.StaticType TypeOfName(string key)
            {
                if (known.TryGetValue(key, out var type)) return type;
                type = Compiler.StaticType.Unknown;
                var separator = key.IndexOf("::", System.StringComparison.Ordinal);
                var global = separator < 0 ? key : key.Substring(0, separator);
                if (ConstantNames.Contains(global) && !UserMemoryScope.TryGet(global, out _))
                {
                    if (separator < 0)
                    {
                        // A cycle (`const A = B + 1`, `const B = A + 1`)
                        // reads a constant before its declaration runs.
                        if (_constantTypes!.TryGetValue(key, out var typeOf) && visiting.Add(key))
                        {
                            type = typeOf(TypeOfName);
                            visiting.Remove(key);
                        }
                    }
                    else if (_enumMembers != null && _enumMembers.TryGetValue(global, out var members))
                    {
                        var member = key.Substring(separator + 2);
                        if (member == "name[]") type = Compiler.StaticType.Text;
                        else if (member == "values" || member == "names") type = Compiler.StaticType.Array;
                        else if (members.Contains(member)) type = Compiler.StaticType.Number;
                    }
                }
                known[key] = type;
                return type;
            }

            foreach (var (check, file, source, line, column) in _typedOperands)
            {
                var message = check(TypeOfName);
                if (message != null)
                    ReportCompileError(new Exception.CompileError(message, file, line, column).LocateIn(file, source));
            }
        }

        /// <summary>
        /// Records an error of a module-set check (T3.8), unless the same
        /// message was already recorded at the same place (code the compiler
        /// emits twice, an ensure body inlined where a <c>break</c>/<c>next</c>
        /// leaves it, records its uses twice).
        /// </summary>
        private void ReportCompileError(Exception.SyntaxException error)
        {
            var reason = error is Exception.CompileError c ? c.Reason : error.Message;
            foreach (var e in _compileErrors!)
                if ((e is Exception.CompileError ce ? ce.Reason : e.Message) == reason && e.ScriptName == error.ScriptName
                    && e.Line == error.Line && e.Column == error.Column)
                    return;
            _compileErrors.Add(error);
        }

        /// <summary>
        /// Records an error the compiler found in the file being compiled
        /// (<see cref="ParsingFile"/>) and recovers from at the end of the
        /// statement or definition it is in (T3.8): true while
        /// <see cref="Compile"/> runs, which then fails with every error
        /// recorded; false otherwise (a source import of
        /// <see cref="LoadBytecode"/>), when the caller rethrows it.
        /// </summary>
        internal bool RecoverFromCompileError(Exception.SyntaxException error)
        {
            if (_compileErrors == null) return false;
            error.LocateIn(ParsingFile, ParsingSource);
            ReportCompileError(error);
            return true;
        }

        /// <summary>
        /// Records a syntax error the parser recovers from by skipping the
        /// statement it is in (T3.8), as <see cref="RecoverFromCompileError"/>
        /// does; never for the v1 frontend of the migration tool.
        /// </summary>
        internal bool RecoverFromParseError(Exception.SyntaxException error) =>
            !ParsingV1 && RecoverFromCompileError(error);

        /// <summary>The number of errors recorded so far while <see cref="Compile"/> runs (0 otherwise).</summary>
        internal int CompileErrorCount => _compileErrors?.Count ?? 0;

        /// <summary>The first error recorded after the first <paramref name="count"/>, or null.</summary>
        internal Exception.SyntaxException? CompileErrorSince(int count) =>
            _compileErrors != null && _compileErrors.Count > count ? _compileErrors[count] : null;

        /// <summary>True while <see cref="Compile"/> runs once an error has been recorded: the compile fails, so the bytecode is not verified.</summary>
        internal bool HasCompileErrors => _compileErrors != null && _compileErrors.Count > 0;

        /// <summary>
        /// The result of a compile that failed with <paramref name="errors"/>
        /// (the first is the one the entry points report, set as
        /// <c>_compileError</c>), followed by <paramref name="warnings"/>.
        /// </summary>
        private CompileResult FailedCompile(List<Exception.SyntaxException> errors, List<CompileDiagnostic> warnings)
        {
            var diagnostics = new List<CompileDiagnostic>(errors.Count + warnings.Count);
            foreach (var error in errors)
            {
                error.LocateIn(ScriptName, _sourceCode);
                diagnostics.Add(Diagnose(error));
            }
            diagnostics.AddRange(warnings);
            _compileError = errors[0];
            return new CompileResult(false, diagnostics.ToArray());
        }

        /// <summary><c>1 argument</c>, <c>0 or 2 arguments</c>, <c>0, 1 or 2 arguments</c>.</summary>
        private static string ArityList(SortedSet<int> arities)
        {
            var text = "";
            int i = 0, last = 0;
            foreach (var a in arities)
            {
                if (i > 0) text += i == arities.Count - 1 ? " or " : ", ";
                text += a.ToString(System.Globalization.CultureInfo.InvariantCulture);
                last = a;
                i++;
            }
            return text + (arities.Count == 1 && last == 1 ? " argument" : " arguments");
        }

        /// <summary>
        /// <c>Class 'C' takes 2 arguments, not 3</c>: a <c>new</c> with the
        /// wrong number of arguments, at compile time (T3.8, PROP-29) or, as
        /// an ArgumentError, at runtime.
        /// </summary>
        internal static string ConstructorArityMessage(string className, string takes, int given) =>
            $"Class '{className}' takes {takes}, not {given.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        /// <summary>
        /// True only during <see cref="ParseForMigration"/>: the parser
        /// accepts v1 syntax, which v2 rejects: imports anywhere, <c>yield</c>
        /// in a class body, <c>this</c> in a lambda; <c>global</c> is lexed as
        /// a keyword but handed to the parser as a name.
        /// </summary>
        internal bool ParsingV1;

        /// <summary>
        /// Lexes and parses the script without compiling it, for the
        /// migration tool (T3.2, plan 8.2): returns the token list, the
        /// top-level statements and the definitions (the instance's own
        /// definition scope, which keeps them), recording every expression's
        /// span into <paramref name="spans"/>. Throws the parser's
        /// <see cref="Exception.SyntaxException"/>. The instance is not
        /// usable afterwards.
        /// </summary>
        internal (IReadOnlyList<Token.Token> Tokens, CompositeStatement TopLevel, DefinitionScope Definitions) ParseForMigration(
            Dictionary<Expression.IExpression, Expression.ExpressionSpan> spans)
        {
            ExpressionSpans = spans;
            ParsingV1 = true;
            DefinitionContext.PushScope(_definitionScope);
            try
            {
                // `global` was a name in v1 (T3.3 made it a keyword). The
                // cached list is shared, so the renamed tokens are a copy.
                var lexed = LexicalParser.Parse(_sourceCode);
                var tokens = new List<Token.Token>(lexed.Count);
                foreach (var t in lexed)
                    tokens.Add(t.Type == Token.TokenType.Keyword && t.Value == "global"
                        ? new Token.Token(Token.TokenType.Variable, t.Value, t.RowNumber, t.Column)
                        : t);
                var statement = new CompositeStatement(this, null, ScriptName);
                StatementParser.Parse(this, tokens, statement);
                return (tokens, statement, _definitionScope);
            }
            catch (Exception.SyntaxException e)
            {
                e.LocateIn(ScriptName, _sourceCode);
                throw;
            }
            finally
            {
                ExpressionSpans = null;
                ParsingV1 = false;
                DefinitionContext.EndScope();
            }
        }

        private void RollBackCompile(HashSet<string> constantsBefore)
        {
            _definitionScope.Clear();
            ClearResolvedImports();
            _cachedCompiled = null;
            ConstantNames.Clear();
            ConstantNames.UnionWith(constantsBefore);
        }

        private CompileDiagnostic Diagnose(Exception.SyntaxException e) =>
            new CompileDiagnostic(DiagnosticSeverity.Error,
                e is Exception.CompileError limit ? limit.Reason : e.Message,
                e.ScriptName ?? ScriptName, e.Line, e.Column);

        /// <summary>
        /// Compile when needed; when the script does not compile, throw its
        /// error. Used by <see cref="Reload"/> and <see cref="SaveBytecode"/>,
        /// which keep v1's exception; the entry points return it as an
        /// <c>Error</c> result instead (<see cref="CompileFailure"/>).
        /// </summary>
        private void EnsureCompiled()
        {
            if (!Compile().Success) throw _compileError!;
        }

        /// <summary>
        /// What an entry returns before it runs anything: <c>Halted</c> when
        /// the instance is halted; the <c>Error</c> result (kind
        /// CompileError) when the script does not compile, which halts the
        /// instance (plan T2.2, 6.4); null when it may run.
        /// </summary>
        private ScriptResult? RefuseBeforeRunning()
        {
            if (IsHalted) return HaltedResult();
            var compiled = Compile();
            if (compiled.Success) return null;
            return FinishEntry(ScriptResult.Failed(ScriptError.FromCompile(_compileError!, compiled.Diagnostics[0], InstanceLabel)));
        }

        /// <summary>
        /// The result of an entry that ran: <paramref name="value"/>, or the
        /// error it left raised. Call it before <see cref="EndEntry"/>, which
        /// logs and clears the error at the outermost entry; a nested entry
        /// leaves it raised for the calling script (PROP-1), and its caller
        /// sees it in the result too.
        /// </summary>
        internal ScriptResult EntryResult(WarValue value) =>
            ExceptionContext.IsRaised()
                ? ScriptResult.Failed(ScriptError.FromRaised(ExceptionContext.Exception, ScriptName, InstanceLabel, _sourceCode))
                : ScriptResult.Ok(value);

        /// <summary>
        /// An entry that cannot start a function: raised for the calling
        /// script when nested inside another entry (like a callback's error,
        /// PROP-1); at the outermost level an <c>Error</c> result that halts
        /// the instance, like any unhandled error.
        /// </summary>
        private ScriptResult RefuseEntry(ScriptErrorKind kind, string message)
        {
            if (EntryDepth == 0)
                return FinishEntry(ScriptResult.Failed(new ScriptError(kind, message, WarValue.FromText(message), ScriptName, InstanceLabel, 0, 0)));
            ExceptionContext.RaiseException(message, kind);
            return EntryResult(WarValue.Null);
        }

        /// <summary>
        /// Recursively null out all AST Statement references in a DefinitionScope tree.
        /// After compilation, only the bytecode is needed — the AST can be GC'd.
        /// </summary>
        private static void DiscardAST(DefinitionScope scope)
        {
            foreach (var f in scope.Functions)
            {
                if (f is NativeFunctionDefinition) continue;
                f.Statement = null;
            }
            foreach (var c in scope.ClassDefinitions)
            {
                c.Statement = null;
                DiscardAST(c.GetDefinitionScope());
            }
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

        /// <summary>
        /// The VM a host entry runs on: the cached one, or a new one when the
        /// cached VM is busy because this entry is nested inside another
        /// (a native calling back into the instance). A busy VM is never
        /// reset (T1.5, DEC-16; pooling is F-VM-2).
        /// </summary>
        private WarVM AcquireVM()
        {
            var vm = EnsureVM();
            return vm.IsRunning ? new WarVM(this) : vm;
        }

        /// <summary>
        /// True when the cached VM (if any) holds no execution state.
        /// Test invariant (see <see cref="WarVM.IsIdle"/>).
        /// </summary>
        internal bool IsVMIdle => _cachedVM == null || _cachedVM.IsIdle;

        // ────────────────────────────────────────────────────────
        //  Hot reload — swap source code, preserve runtime state
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Replace the script source code with new code, re-parse, and re-compile
        /// all functions and classes — without re-executing top-level statements
        /// and without clearing global variable state.
        ///
        /// After Reload():
        /// - All function definitions are updated to the new source.
        ///   The next Call() will execute the new bytecode.
        /// - Global variables (hp, entities, arrays, etc.) are preserved.
        /// - Existing class instances keep their old definition. New instances
        ///   created after reload use the new class definition.
        /// - All active coroutines are stopped (they reference old bytecode).
        /// - Function handles obtained via GetFunction() call the new
        ///   definition (plan T2.6); a handle whose function the new source
        ///   no longer defines returns an Error result (NameError).
        ///
        /// Typical usage during development:
        /// <code>
        ///   // File watcher detects a change
        ///   var newSource = File.ReadAllText("patrol.ws");
        ///   script.Reload(newSource);
        ///   script.Call(tickFunc, dt);  // the same handle runs the new tick
        /// </code>
        /// </summary>
        public void Reload(string newSourceCode)
        {
            // 1. Stop all coroutines — they hold VMs with old bytecode —
            //    and return a halted instance to normal operation (plan 6.4)
            ClearCoroutines();
            ClearFault();

            // 2. Clear old definitions (functions, classes)
            //    The MemoryScope (_memoryScope) is deliberately NOT cleared —
            //    global variables and class instance state survive the reload.
            _definitionScope.Clear();

            // 3. Clear all caches
            _compileResult = null;
            _compileError = null;
            _cachedCompiled = null;
            _cachedVM = null;
            ImportCache.Clear();
            ClearResolvedImports();
            ClearScriptConstantNames();

            // 4. Clear any lingering exception state
            if (ExceptionContext.IsRaised())
                ExceptionContext.RescueException();

            // 5. Re-lex, re-parse and re-compile (but do NOT execute
            //    top-level code). A compile error is thrown, as in v1.
            _sourceCode = newSourceCode;
            EnsureCompiled();
        }

        // ────────────────────────────────────────────────────────
        //  Bytecode serialization — skip parse+compile at load time
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Save the compiled bytecode to a binary stream. The script must
        /// have been compiled first (it compiles when needed; see Compile()).
        ///
        /// Typical build-time usage:
        /// <code>
        ///   var script = new WarScriptLanguage("patrol", source, null, null);
        ///   script.Run();
        ///   using var fs = File.Create("patrol.wsbc");
        ///   script.SaveBytecode(fs);
        /// </code>
        /// </summary>
        public void SaveBytecode(Stream stream)
        {
            // Ensure we have something to save
            CompileWithoutRunning();

            using var w = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            BytecodeSerializer.Save(w, _cachedCompiled!, _definitionScope, _imports);
        }

        /// <summary>
        /// Parses and compiles the script (cached) without running it, and
        /// returns the script body and the scope holding its functions and
        /// classes. Used by <see cref="SaveBytecode"/> and by tests that
        /// inspect compiler output (T1.7).
        /// </summary>
        internal (CompiledFunction script, DefinitionScope definitions) CompileWithoutRunning()
        {
            EnsureCompiled();
            return (_cachedCompiled!, _definitionScope);
        }

        /// <summary>
        /// Load pre-compiled bytecode from a binary stream, replacing any
        /// existing definitions. This skips the lexer, parser, and compiler
        /// entirely — the script is ready to Call() immediately.
        ///
        /// Global variable state (_memoryScope) is preserved, just like Reload().
        /// Native functions (registered via WarScriptLibraryRegistry) are unaffected.
        ///
        /// The module's imports (its header's import list, T3.7) are resolved
        /// and compiled here, as <see cref="Compile"/> does for source, before
        /// anything is installed: an import that cannot be found, read or
        /// compiled, an import cycle and a definition the module set defines
        /// twice throw <see cref="InvalidDataException"/> (the compile error
        /// is its inner <see cref="Exception.SyntaxException"/>) and leave the
        /// instance unchanged. The imports' functions and classes are the
        /// script's once it loads; <see cref="Run"/> runs their top-level
        /// code before the script's.
        ///
        /// Typical runtime usage:
        /// <code>
        ///   using var fs = File.OpenRead("patrol.wsbc");
        ///   script.LoadBytecode(fs);
        ///   var tick = script.GetFunction("tick", 1);
        ///   script.Call(tick, dt);
        /// </code>
        /// </summary>
        public void LoadBytecode(Stream stream)
        {
            // Read and verify everything first: a file that is rejected
            // (InvalidDataException) leaves this instance unchanged (10.4).
            CompiledFunction topLevel;
            DefinitionScope scope;
            IReadOnlyList<string> imports;
            using (var r = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                (topLevel, scope) = BytecodeSerializer.Load(r, this, GlobalDefinitionScope, out imports);

            // Resolve its imports (T3.7) under the loaded scope; a failure
            // puts back what resolving changed.
            var constantsBefore = new HashSet<string>(ConstantNames);
            var resolvedBefore = new List<ImportedModule>(ImportOrder);
            ClearScriptConstantNames();
            ClearResolvedImports();
            List<DefinitionScope> importScopes;
            try
            {
                importScopes = ResolveLoadedImports(imports, scope);
            }
            catch (System.Exception e)
            {
                ConstantNames.Clear();
                ConstantNames.UnionWith(constantsBefore);
                ClearResolvedImports();
                foreach (var module in resolvedBefore) AddResolvedImport(module);
                if (e is Exception.SyntaxException syntax)
                    throw new InvalidDataException(syntax.Message, syntax);
                throw;
            }

            // Stop coroutines referencing old bytecode (a halted instance
            // stays halted: only Reload and ClearFault clear the fault)
            ClearCoroutines();

            // Clear old definitions and caches
            _definitionScope.Clear();
            _cachedCompiled = null;
            _cachedVM = null;
            ImportCache.Clear();

            if (ExceptionContext.IsRaised())
                ExceptionContext.RescueException();

            // Merge loaded definitions into _definitionScope, then the
            // imports' (one namespace, 5.8); an import of the module now
            // falls back to the instance's definitions.
            scope.CopyLocalDefinitionsTo(_definitionScope);
            foreach (var imported in importScopes)
                if (imported.Parent == scope) imported.Reparent(_definitionScope);
            foreach (var imported in importScopes)
                imported.CopyImportedDefinitionsTo(_definitionScope);
            _imports = imports;
            _cachedCompiled = topLevel;
            _compileResult = CompileResult.Succeeded;
            _compileError = null;
        }

        /// <summary>
        /// Ask <see cref="BytecodeResolver"/> for the precompiled form of an
        /// imported path and deserialize it into a scope of its own.
        ///
        /// Returns the imported file's top-level bytecode — which the caller
        /// still has to run, exactly like the compiled-from-source form — or
        /// null when there is nothing to use. Null is the ordinary answer: a
        /// host with no resolver, one that has no bytecode for this path, and
        /// one whose bytecode will not load all say "compile the source", and
        /// only the last of those is worth a log line. Also returns the
        /// loaded file's import list (empty when nothing was loaded).
        /// </summary>
        internal CompiledFunction? LoadImportedBytecode(
            string path, DefinitionScope parentScope, out DefinitionScope? scope, out IReadOnlyList<string> imports)
        {
            scope = null;
            imports = System.Array.Empty<string>();
            if (BytecodeResolver == null) return null;

            byte[]? bytes;
            try
            {
                bytes = BytecodeResolver(path);
            }
            catch (System.Exception e)
            {
                // A throwing resolver is the host's bug, not the script's: the
                // source is still worth trying, so this reports and falls back
                // rather than failing the import outright.
                Log(
                    $"[WarScript] Bytecode resolver failed for import '{path}' ({e.Message}) — " +
                    "compiling its source instead.");
                return null;
            }

            if (bytes == null || bytes.Length == 0) return null;

            try
            {
                using var stream = new MemoryStream(bytes, false);
                using var r = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                var (topLevel, loaded) = BytecodeSerializer.Load(r, this, parentScope, out var loadedImports);
                scope = loaded;
                imports = loadedImports;
                return topLevel;
            }
            catch (System.Exception e)
            {
                Log(
                    $"[WarScript] Precompiled bytecode for import '{path}' could not be loaded " +
                    $"({e.Message}) — compiling its source instead.");
                return null;
            }
        }

        // ────────────────────────────────────────────────────────
        //  Run — execute the full script via bytecode VM
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Compile the script if needed (<see cref="Compile"/>), then execute
        /// its top-level code (plan 6.2). Returns <c>Ok</c> with a null value
        /// when it completes; <c>Error</c> when a script error escapes it, or
        /// with kind CompileError, before anything runs, when the script does
        /// not compile (or, while it runs, a file it imports); <c>HostFault</c>
        /// for a C# exception inside the VM. An error or host fault halts the
        /// instance and is logged (plan 6.4); a halted instance returns
        /// <c>Halted</c> without running anything. Never throws for a script
        /// error (plan T2.3).
        /// </summary>
        public ScriptResult Run()
        {
            var refused = RefuseBeforeRunning();
            if (refused != null) return refused.Value;
            if (!BeginEntry()) return EntryResult(WarValue.Null);
            DefinitionContext.PushScope(_definitionScope);

            ScriptResult result;
            try
            {
                var vm = AcquireVM();
                try
                {
                    vm.Run(_cachedCompiled!);
                    result = EntryResult(WarValue.Null);
                }
                catch (System.Exception e) { result = HostFaultAtEntry(e); }
            }
            finally
            {
                DefinitionContext.EndScope();
                EndEntry();
            }
            return FinishEntry(result);
        }

        // ────────────────────────────────────────────────────────
        //  Call — host invokes a WarScript function (tick, events)
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Call a WarScript function from the host (C#/Unity).
        /// This is the hot path for game integration — tick loops, event handlers.
        /// The handle calls the function its name and arity resolve to now
        /// (plan T2.6), so it survives <see cref="Reload"/>; when the
        /// instance no longer defines one, the result is an <c>Error</c>
        /// (NameError), like any refused entry. A handle of another instance
        /// is a host error (<see cref="ArgumentException"/>).
        /// Every script function of the instance has bytecode once the script
        /// is compiled (<see cref="Run"/>, <see cref="Reload"/>,
        /// <see cref="LoadBytecode"/>). A function without bytecode (a native
        /// function) cannot be called this way: the call does nothing but
        /// return an <c>Error</c> result (TypeError) — there is no AST
        /// interpreter to run it (plan T2.1).
        /// Returns <c>Ok</c> with the function's return value, or the error
        /// that escaped it, as <see cref="Run"/> does (plan T2.3). Called from
        /// a native while the instance runs (a nested entry, plan 6.5), an
        /// error result's error also stays raised: it continues in the calling
        /// script from the native call site (PROP-1). At the outermost level an
        /// error halts the instance (plan 6.4), and a halted instance returns
        /// <c>Halted</c>.
        /// </summary>
        public ScriptResult Call(FunctionHandle function, params WarValue[] arguments) =>
            Call(function, new ReadOnlySpan<WarValue>(arguments));

        /// <summary>
        /// <see cref="Call(FunctionHandle, WarValue[])"/> without the
        /// <c>params</c> array (plan T2.7): the arguments are copied onto the
        /// VM stack and the span is not kept, so a host can call every tick
        /// from one reused buffer (<c>args.AsSpan(0, n)</c>) without
        /// allocating. Missing arguments are null (so default parameters
        /// apply); arguments beyond the function's arity are ignored.
        /// </summary>
        public ScriptResult Call(FunctionHandle function, ReadOnlySpan<WarValue> arguments)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (!ReferenceEquals(function.Instance, this))
                throw new ArgumentException(System.FormattableString.Invariant(
                    $"The handle of '{function}' belongs to the instance '{function.Instance.ScriptName}', not '{ScriptName}'"),
                    nameof(function));
            var refused = RefuseBeforeRunning();
            if (refused != null) return refused.Value;
            var definition = function.Definition;
            if (definition == null)
                return RefuseEntry(ScriptErrorKind.NameError, System.FormattableString.Invariant(
                    $"Function '{function.Name}' with {function.Arity} args is not defined"));
            if (definition.Compiled != null)
                return CallBytecode(definition, arguments);

            return RefuseEntry(ScriptErrorKind.TypeError, System.FormattableString.Invariant(
                $"Cannot call '{definition.Details.Name}' in '{ScriptName}': it has no bytecode"));
        }

        private ScriptResult CallBytecode(FunctionDefinition function, ReadOnlySpan<WarValue> arguments)
        {
            if (!BeginEntry()) return EntryResult(WarValue.Null);
            DefinitionContext.PushScope(_definitionScope);

            ScriptResult result;
            try
            {
                var vm = AcquireVM();
                try { result = EntryResult(vm.RunFunction(function.Compiled!, arguments)); }
                catch (System.Exception e) { result = HostFaultAtEntry(e); }
            }
            finally
            {
                DefinitionContext.EndScope();
                EndEntry();
            }
            return FinishEntry(result);
        }

        /// <summary>
        /// The last host fault: a C# exception that escaped the VM, other
        /// than a script error (plan T1.3, 6.3). Set by the entry point that
        /// stopped because of it; the golden runner reports it as such.
        /// </summary>
        internal System.Exception? LastHostFault { get; private set; }

        /// <summary>
        /// A host fault caught by an entry point, before <see cref="EndEntry"/>.
        /// The VM has restored the context stacks and reset itself. The
        /// outermost entry reports it (<see cref="ReportHostFault"/>; its
        /// result then halts the instance). A nested entry records it and
        /// returns the <c>HostFault</c> result to the native that called it;
        /// when that native returns, the calling VM rethrows the fault, so
        /// every entry around it stops (plan 6.3, T2.4).
        /// </summary>
        internal ScriptResult HostFaultAtEntry(System.Exception e)
        {
            if (EntryDepth <= 1) return ReportHostFault(e);
            if (_pendingHostFault == null) _pendingHostFault = e;
            if (ExceptionContext.IsRaised())
                ExceptionContext.RescueException();
            return ScriptResult.Failed(ScriptError.FromHostFault(e, ScriptName, InstanceLabel));
        }

        /// <summary>The <c>HostFault</c> result of the outermost entry; it is logged when it halts the instance.</summary>
        private ScriptResult ReportHostFault(System.Exception e)
        {
            LastHostFault = e;
            _pendingHostFault = null;
            if (ExceptionContext.IsRaised())
                ExceptionContext.RescueException();
            return ScriptResult.Failed(ScriptError.FromHostFault(e, ScriptName, InstanceLabel));
        }

        // ────────────────────────────────────────────────────────
        //  Output and state hash
        // ────────────────────────────────────────────────────────

        /// <summary>Output of the <c>print</c> builtin: recorded for the state hash, then logged.</summary>
        internal void Print(string line)
        {
            _printedHash = StateHash.AppendLine(_printedHash, line);
            Logger?.Invoke(this, line);
        }

        /// <summary>
        /// A 64-bit hash of everything the instance has printed since it was
        /// created and of the current values of its user globals (host globals
        /// such as <c>[WsConst]</c> values are excluded). Identical inputs give
        /// identical hashes on every machine, so comparing them per tick
        /// locates a desync. The encoding is fixed by the v2 plan, section 9.7.
        /// </summary>
        public ulong ComputeStateHash() => StateHash.Compute(_printedHash, _memoryScope);

        // ────────────────────────────────────────────────────────
        //  Function lookup
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// A handle to the function of this name and arity (plan T2.6), or
        /// null when the instance does not define one now (before the script
        /// is compiled, only natives are defined). The handle stays valid
        /// across <see cref="Reload"/> and <see cref="LoadBytecode"/>: it
        /// calls whatever the instance defines under that name and arity at
        /// the time of the call.
        /// </summary>
        public FunctionHandle? GetFunction(string functionName, int arguments)
        {
            var definition = _definitionScope.GetFunction(functionName, arguments);
            return definition == null ? null : new FunctionHandle(this, functionName, arguments, definition);
        }

        /// <summary>The definition a name and arity resolve to now (script function or native), or null.</summary>
        internal FunctionDefinition? FindFunction(string functionName, int arguments) =>
            _definitionScope.GetFunction(functionName, arguments);

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

        public void SetYielded(YieldType type, F64 waitDuration)
        {
            IsYielded = true;
            YieldedType = type;
            YieldedWaitDuration = waitDuration;
        }

        public void ClearYield()
        {
            IsYielded = false;
            YieldedType = YieldType.NextTick;
            YieldedWaitDuration = F64.Zero;
        }

        // ────────────────────────────────────────────────────────
        //  Coroutine support
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Start a coroutine on a script function and run it to its first
        /// yield; <paramref name="id"/> is its id, or -1 when there is no such
        /// script function (an <c>Error</c> result: NameError, or TypeError for
        /// a native function). The result is the first run's: <c>Ok</c> (null
        /// value) when it yielded or completed, else its error, which also
        /// ends the coroutine. Compiles the script if needed, so it can be
        /// called before <see cref="Run"/> (the top-level code has then not
        /// run); a compile error is an <c>Error</c> result, as from
        /// <see cref="Run"/>. Started from a native while the instance runs,
        /// an error (a missing function's, or the first run's) stays raised
        /// for the calling script (PROP-1); from the host it halts the
        /// instance (plan 6.4), and a halted instance returns <c>Halted</c>.
        /// </summary>
        public ScriptResult StartCoroutine(string functionName, WarValue[] args, bool loop, out int id) =>
            StartCoroutine(functionName, new ReadOnlySpan<WarValue>(args), loop, out id);

        /// <summary>
        /// <see cref="StartCoroutine(string, WarValue[], bool, out int)"/>
        /// taking the arguments as a span (plan 6.1, T2.7). The function is
        /// looked up by the span's length. The coroutine copies the arguments
        /// (a looping coroutine restarts with them), so the caller may reuse
        /// its buffer as soon as this returns.
        /// </summary>
        public ScriptResult StartCoroutine(string functionName, ReadOnlySpan<WarValue> args, bool loop, out int id)
        {
            id = -1;
            var refused = RefuseBeforeRunning();
            if (refused != null) return refused.Value;
            var argCount = args.Length;
            var function = _definitionScope.GetFunction(functionName, argCount);
            if (function == null)
                return RefuseEntry(ScriptErrorKind.NameError,
                    System.FormattableString.Invariant($"Coroutine function '{functionName}' with {argCount} arguments is not defined"));
            if (function.Compiled == null)
            {
                // A native function: only script functions can be suspended.
                return RefuseEntry(ScriptErrorKind.TypeError,
                    System.FormattableString.Invariant($"Coroutine function '{functionName}' with {argCount} arguments has no bytecode"));
            }

            id = _nextCoroutineId++;
            var bco = new BytecodeCoroutine(
                this, function.Compiled, _definitionScope,
                args.ToArray(), loop, id);
            _coroutines.Add(bco);
            return bco.Resume();
        }

        /// <summary>
        /// <see cref="StartCoroutine(string, WarValue[], bool, out int)"/> for
        /// a caller that only wants the id (-1: nothing started); an error
        /// halts the instance (and is logged), or stays raised when started
        /// from a native, but is not returned.
        /// </summary>
        public int StartCoroutine(string functionName, WarValue[] args, bool loop = false)
        {
            StartCoroutine(functionName, args, loop, out var id);
            return id;
        }

        public bool StopCoroutine(int id)
        {
            for (var i = _coroutines.Count - 1; i >= 0; i--)
            {
                if (_coroutines[i].Id == id)
                {
                    _coroutines.RemoveAt(i);
                    _coroutinesRemoved++;
                    return true;
                }
            }
            return false;
        }

        public void StopAllCoroutines() => ClearCoroutines();

        private void ClearCoroutines()
        {
            _coroutines.Clear();
            _coroutinesRemoved++;
        }

        /// <summary>
        /// Counts removals from <see cref="_coroutines"/> other than the
        /// tick's own (stop, stop all, reload, load, halt), so a tick can tell
        /// whether a resume stopped coroutines it has not reached yet.
        /// </summary>
        private int _coroutinesRemoved;

        /// <summary>
        /// Advance every coroutine's wait by <paramref name="dt"/> and resume
        /// the ready ones, newest first (PROP-24); each resume is a host
        /// entry. Returns <c>Ok</c> with the number of active coroutines as
        /// its value, or the first error a resume ended with: the tick stops
        /// there and the remaining coroutines do not run (PROP-3). From the
        /// host such an error halts the instance, which clears every
        /// coroutine; a halted instance returns <c>Halted</c>. Coroutines
        /// started during the tick first run on the next one; coroutines
        /// stopped during it (by a native, or a nested <see cref="Reload"/>)
        /// are not resumed.
        /// </summary>
        public ScriptResult TickCoroutines(F64 dt)
        {
            if (IsHalted) return HaltedResult();
            var count = _coroutines.Count;
            if (count == 0) return ScriptResult.Ok(WarValue.FromNumeric(0));

            // A resume may start, stop or clear coroutines (natives, nested
            // entries), so walk a snapshot of the list as it was.
            var snapshot = System.Buffers.ArrayPool<ICoroutine>.Shared.Rent(count);
            ScriptResult? failed = null;
            try
            {
                _coroutines.CopyTo(snapshot, 0);
                var removedBefore = _coroutinesRemoved;
                for (var i = count - 1; i >= 0; i--)
                {
                    var co = snapshot[i];
                    if (co.IsComplete) continue;
                    if (_coroutinesRemoved != removedBefore && !_coroutines.Contains(co)) continue;
                    if (!co.IsReady(dt)) continue;
                    var result = co.Resume();
                    if (!result.IsOk)
                    {
                        failed = result;
                        break;
                    }
                }
            }
            finally
            {
                System.Array.Clear(snapshot, 0, count);
                System.Buffers.ArrayPool<ICoroutine>.Shared.Return(snapshot);
            }

            // Drop the coroutines that completed (in place, keeping the order).
            var kept = 0;
            for (var i = 0; i < _coroutines.Count; i++)
            {
                var co = _coroutines[i];
                if (co.IsComplete) continue;
                _coroutines[kept++] = co;
            }
            _coroutines.RemoveRange(kept, _coroutines.Count - kept);

            return failed ?? ScriptResult.Ok(WarValue.FromNumeric(_coroutines.Count));
        }

        public int ActiveCoroutineCount => _coroutines.Count;
    }
}
