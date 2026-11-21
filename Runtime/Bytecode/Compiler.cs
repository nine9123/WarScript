#nullable enable

using System.Collections.Generic;
using WarScript.Context.Definition;
using WarScript.Exception;
using WarScript.Expression;
using WarScript.Expression.Operator;
using WarScript.Expression.Value;
using WarScript.Statement;
using WarScript.Statement.Loop;

namespace WarScript.Bytecode
{
    /// <summary>
    /// Single-pass compiler that walks the AST and emits bytecode into a Chunk.
    /// Each function gets its own CompiledFunction via a child Compiler instance.
    /// </summary>
    public class Compiler
    {
        private struct Local
        {
            public string Name;
            public int Depth;
            public LocalKind Kind;
        }

        /// <summary>What a stack slot holds (T3.4).</summary>
        private enum LocalKind
        {
            /// <summary>A compiler temporary (<c>$limit</c>, <c>$items</c>...).</summary>
            Hidden,
            Parameter,
            /// <summary>A name assigned in a function, method or lambda body: local to the whole body (PROP-9).</summary>
            BodyLocal,
            /// <summary>A loop or rescue variable: exists only inside its block.</summary>
            Block,
        }

        private struct LoopCtx
        {
            public int LoopStart;
            public int ContinueTarget; // for while loops only (known at push time)
            public List<int> BreakJumps;
            public List<int> NextJumps;  // forward jumps patched to increment position
            public int ScopeDepth;
            public int LocalCountAtLoop;
            public bool UsesForwardNext; // true for for/iterable loops
        }

        private readonly WarScriptLanguage _script;
        private CompiledFunction _current = null!;
        /// <summary>The script being compiled, recorded in every chunk (<see cref="Chunk.Source"/>).</summary>
        private string? _source;
        private Chunk Chunk => _current.Chunk;

        /// <summary>
        /// The source line of the statement being compiled (T1.10, DX-3).
        /// Expressions carry no line of their own, so everything emitted for
        /// one gets its statement's line. A statement without a line (one the
        /// parser desugared) keeps the line of the code around it.
        /// </summary>
        private int _line;

        /// <summary>
        /// Column of the statement being compiled (T3.1), 0 when unknown;
        /// set and restored with <see cref="_line"/> by CompileStatement, and
        /// moved to an operator, call, <c>new</c> or index on that line while
        /// CompileExpression compiles it. Compile errors are reported there,
        /// and every byte emitted is marked with it (<see cref="SetColumn"/>).
        /// </summary>
        private int _column;

        /// <summary>Set <see cref="_column"/> and the column the chunk marks emitted bytes with.</summary>
        private void SetColumn(int column)
        {
            _column = column;
            _current.Chunk.Column = column;
        }

        private readonly List<Local> _locals = new();
        private int _scopeDepth;
        private int _maxLocals;
        private readonly List<LoopCtx> _loops = new();

        /// <summary>Where in a begin block the code being compiled is.</summary>
        private enum TryRegion { Begin, Rescue, Ensure }

        /// <summary>
        /// A begin block being compiled. <c>break</c> and <c>next</c> use it
        /// to leave the block: remove its handler, discard what its ensure
        /// block was going to re-raise or return, and run its ensure block
        /// (Q-12, CRASH-8).
        /// </summary>
        private sealed class TryCtx
        {
            public int LoopCount;          // _loops.Count when the block started
            public int LocalCount;         // outside the block
            public TryRegion Region;
            public CompositeStatement? Ensure;
            public readonly List<int> DropPendingPatches = new();
        }

        private readonly List<TryCtx> _tries = new();
        private readonly List<string> _localNames = new();

        /// <summary>
        /// 0 = top-level script body (variables go to globals via MemoryScope).
        /// > 0 = inside a function (variables become stack locals).
        /// </summary>
        private readonly int _functionDepth;
        private int _tryDepth;

        // ── Name resolution (T3.4, DEC-5, PROP-9, PROP-10, PROP-16) ──

        /// <summary>
        /// True while compiling a function, method or lambda body: every
        /// name assigned in it (without <c>global</c>) is a stack slot of
        /// the whole body, and the order rules below are checked. False for
        /// top-level code (whose names are globals) and class bodies.
        /// </summary>
        private bool _resolveNames;

        /// <summary>
        /// True while compiling a class body (a constructor, T3.4, 5.7): a
        /// bare assignment declares a field of the instance
        /// (<c>ThisSetProperty</c>) unless the name is declared
        /// <c>global</c>; a bare read finds a field first, then a global.
        /// </summary>
        private bool _classBody;

        /// <summary>The names the body declares <c>global</c> (function, method, lambda or class body).</summary>
        private HashSet<string> _declaredGlobals = new();

        /// <summary>
        /// The fields of the class whose method or body is being compiled:
        /// constructor parameters, names assigned in the class body and the
        /// fields of its base classes (5.7). <c>@x</c> / <c>this :: x</c> is
        /// checked against it at compile time (DEC-6). Null outside a class,
        /// or when a base class is not known at compile time (one from an
        /// import, until static imports, T3.7): then nothing is checked.
        /// </summary>
        private HashSet<string>? _classFields;

        /// <summary>The name of the class whose method or body is being compiled, for <see cref="_classFields"/> errors.</summary>
        private string? _className;

        /// <summary>The body's locals assigned so far, in source order (read before assignment, PROP-9).</summary>
        private readonly HashSet<string> _assignedSoFar = new();

        /// <summary>Every bare name the body has read or written so far (<c>global</c> must come first, PROP-10).</summary>
        private readonly HashSet<string> _usedNames = new();

        /// <summary>The body locals read somewhere in the body (PROP-26: the others are reported unused).</summary>
        private readonly HashSet<string> _readLocals = new();

        /// <summary>Where each body local is first assigned in the source: the position of its PROP-26 warning.</summary>
        private readonly Dictionary<string, (int Line, int Column)> _firstAssignments = new();

        /// <summary>
        /// Positive while an ensure body is compiled a second time where a
        /// <c>break</c>/<c>next</c> leaves its block: the order checks were
        /// made where the body is in the source.
        /// </summary>
        private int _inlinedEnsure;

        /// <summary>
        /// For a lambda: the names that are stack slots of the code it is
        /// written in (and of that code's own enclosing code). A lambda has
        /// no closures, so reading one is a compile error (PROP-16).
        /// </summary>
        private HashSet<string>? _enclosingLocals;

        /// <summary>True once this body recovered from a compile error (<see cref="Recover"/>).</summary>
        private bool _recovered;

        // ────────────────────────────────────────────────────────
        //  Public entry point
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Compile a top-level script. Compiles all definitions in the scope tree,
        /// then compiles the main body statements.
        /// </summary>
        public static CompiledFunction CompileScript(
            WarScriptLanguage script,
            CompositeStatement program,
            DefinitionScope userScope)
        {
            var compiler = new Compiler(script, functionDepth: 0);
            return compiler.DoCompileScript(program, userScope);
        }

        private Compiler(WarScriptLanguage script, int functionDepth)
        {
            _script = script;
            _functionDepth = functionDepth;
        }

        private CompiledFunction DoCompileScript(CompositeStatement program, DefinitionScope defScope)
        {
            _source = program.BlockName;
            _current = new CompiledFunction("<script>", 0);
            _current.Chunk.Source = _source;

            // Compile all function/class definitions reachable from this scope
            CompileDefinitions(defScope);

            // Compile main body
            WarnUnreachable(program);
            foreach (var stmt in program.StatementsToExecute)
                CompileStatement(stmt);

            EmitReturn(0);
            _current.LocalCount = _maxLocals;
            _current.LocalNames = _localNames.ToArray();
            Chunk.FinalizePropertyCaches();
            VerifyOutput(_current);
            return _current;
        }

        // ────────────────────────────────────────────────────────
        //  Definition compilation (functions, classes)
        // ────────────────────────────────────────────────────────

        private void CompileDefinitions(DefinitionScope scope)
        {
            // Compile all user-defined functions in this scope
            foreach (var funcDef in scope.Functions)
            {
                if (funcDef is NativeFunctionDefinition) continue;
                try { CompileFunctionDef(funcDef); }
                catch (SyntaxException e) when (_script.RecoverFromCompileError(e)) { }
            }
            // Compile all classes (methods + constructor bodies)
            foreach (var classDef in scope.ClassDefinitions)
                CompileClassDef(classDef);
        }

        private void CompileFunctionDef(FunctionDefinition funcDef, string? className = null, HashSet<string>? classFields = null)
        {
            if (funcDef.Compiled != null) return;
            if (funcDef.Statement == null) return; // native function

            CheckParameterCount(funcDef.Details.Arguments.Count, _source, Line(funcDef.Statement), funcDef.Statement.Column);
            var child = new Compiler(_script, functionDepth: 1);
            child._source = _source;
            child._current = new CompiledFunction(funcDef.Details.Name, funcDef.Details.Arguments.Count);
            child._current.Chunk.Source = _source;
            child._line = Line(funcDef.Statement);
            child.SetColumn(funcDef.Statement.Column);
            child._className = className;
            child._classFields = classFields;

            child.BeginBody(funcDef.Details.Arguments, funcDef.Statement);

            // Compile function body (nested definitions are compiled separately
            // via the flat CompileAllDefinitions walk)
            child.WarnUnreachable(funcDef.Statement);
            foreach (var stmt in funcDef.Statement.StatementsToExecute)
                child.CompileStatement(stmt);
            child.WarnUnreadLocals();

            child.EmitReturn(0);
            child._current.LocalCount = child._maxLocals;
            child._current.LocalNames = child._localNames.ToArray();
            child._current.Chunk.FinalizePropertyCaches();
            VerifyOutput(child._current);
            funcDef.Compiled = child._current;
        }

        private void CompileClassDef(ClassDefinition classDef)
        {
            var classScope = classDef.GetDefinitionScope();
            var fields = ClassFields(classDef, new List<ClassDefinition>());
            var className = classDef.ClassDetails.Name;
            if (classDef.Statement != null) classDef.Fields = OwnFields(classDef);
            // A compile error in the bindings, a method or the class body is
            // recorded and the rest of the class compiled (T3.8).
            if (fields != null)
            {
                try { CheckParameterBindings(classDef); }
                catch (SyntaxException e) when (_script.RecoverFromCompileError(e)) { }
            }

            // Compile all methods
            foreach (var funcDef in classScope.Functions)
            {
                if (funcDef is NativeFunctionDefinition) continue;
                try { CompileFunctionDef(funcDef, className, fields); }
                catch (SyntaxException e) when (_script.RecoverFromCompileError(e)) { }
            }

            // Compile nested classes
            foreach (var nestedClass in classScope.ClassDefinitions)
                CompileClassDef(nestedClass);

            // Compile constructor body (non-definition statements only)
            try { CompileConstructorBody(classDef, fields); }
            catch (SyntaxException e) when (_script.RecoverFromCompileError(e)) { }
        }

        /// <summary>
        /// The fields of <paramref name="classDef"/> (5.7): its constructor
        /// parameters, the names its body assigns (bare, or as <c>@x</c> /
        /// <c>this :: x</c>) and not declared <c>global</c>, and the fields
        /// of its base classes. Null when a class is not known here (a base
        /// defined nowhere this compile sees, or one without source:
        /// registered by the host or loaded from bytecode, a bytecode
        /// import's included), shares its name with a nested class
        /// of the class deriving from it, or is on its own base path (a
        /// runtime error at <c>new</c>, Q-11): nothing can be checked then.
        /// </summary>
        private HashSet<string>? ClassFields(ClassDefinition classDef, List<ClassDefinition> path)
        {
            if (classDef.Statement == null) return null;
            var fields = OwnFields(classDef);
            path.Add(classDef);
            foreach (var baseType in classDef.BaseTypes)
            {
                // The VM resolves a base class from the scope of the 'new'
                // that creates the instance, which does not see this class's
                // nested classes: when one has the base's name, which class
                // the base is depends on that site, so nothing is checked.
                if (DefinesNestedClass(classDef, baseType.Name)) return null;
                var baseDef = BaseClass(classDef, baseType.Name);
                if (baseDef == null || path.Contains(baseDef)) return null;
                var baseFields = ClassFields(baseDef, path);
                if (baseFields == null) return null;
                fields.UnionWith(baseFields);
            }
            path.RemoveAt(path.Count - 1);
            return fields;
        }

        /// <summary>
        /// The fields <paramref name="classDef"/> declares itself: its
        /// constructor parameters and the names its body assigns (bare, or
        /// as <c>@x</c> / <c>this :: x</c>) and does not declare
        /// <c>global</c>. Needs the class's source.
        /// </summary>
        private static HashSet<string> OwnFields(ClassDefinition classDef)
        {
            var fields = new HashSet<string>(classDef.ClassDetails.Properties);
            fields.UnionWith(BodyNames.Collect(classDef.ClassDetails.Properties, classDef.Statement, null, classBody: true).Fields);
            return fields;
        }

        /// <summary>
        /// Q-16: an instance has one field per name (5.7), so a base class may
        /// not bind a parameter to an argument other than the one a field of
        /// that name is already bound to: <c>class C [p, q] : A [p, q], B [q, p]</c>
        /// with <c>A [a, b]</c> and <c>B [a, b]</c> is a compile error, while
        /// passing the same parameter on (<c>class Bird [name] : Animal [name]</c>,
        /// or a diamond) is one field. A binding is a parameter of
        /// <paramref name="classDef"/> or nothing (an argument that is not a
        /// parameter of the deriving class, or none). Only called when every
        /// base class is known (<see cref="ClassFields"/> is not null); the VM
        /// raises the same error, as a TypeError at <c>new</c>, otherwise.
        /// </summary>
        private void CheckParameterBindings(ClassDefinition classDef)
        {
            var parameters = classDef.ClassDetails.Properties;
            var bindings = new Dictionary<string, int>();
            var own = new int[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                own[i] = i;
                bindings[parameters[i]] = i;
            }
            BindBases(classDef, own);

            void BindBases(ClassDefinition derived, int[] derivedBindings)
            {
                var derivedParameters = derived.ClassDetails.Properties;
                foreach (var baseType in derived.BaseTypes)
                {
                    var baseDef = BaseClass(derived, baseType.Name)!;
                    var baseParameters = baseDef.ClassDetails.Properties;
                    var baseBindings = new int[baseParameters.Count];
                    for (int j = 0; j < baseParameters.Count; j++)
                    {
                        var index = j < baseType.Properties.Count ? derivedParameters.IndexOf(baseType.Properties[j]) : -1;
                        var binding = index >= 0 ? derivedBindings[index] : -1;
                        baseBindings[j] = binding;
                        var name = baseParameters[j];
                        if (bindings.TryGetValue(name, out var existing))
                        {
                            if (existing != binding)
                                throw new CompileError(WarVM.BoundTwiceMessage(name, classDef.ClassDetails.Name, baseDef.ClassDetails.Name),
                                    _source, Line(classDef.Statement), classDef.Statement.Column);
                        }
                        else bindings[name] = binding;
                    }
                    BindBases(baseDef, baseBindings);
                }
            }
        }

        /// <summary>
        /// The base class <paramref name="name"/> of <paramref name="derived"/>
        /// as the VM will find it: in the class's scope chain, else in the
        /// imports <see cref="WarScriptLanguage.Compile"/> has resolved so far
        /// (T3.7, 5.8: their classes are the script's once it compiles, and
        /// a class name is defined once in the module set, PROP-12). Null
        /// when neither has it.
        /// </summary>
        private ClassDefinition? BaseClass(ClassDefinition derived, string name)
            => derived.GetDefinitionScope().GetClass(name) ?? _script.FindImportedClass(name);

        private static bool DefinesNestedClass(ClassDefinition classDef, string name)
        {
            foreach (var nested in classDef.GetDefinitionScope().ClassDefinitions)
                if (nested.ClassDetails.Name == name) return true;
            return false;
        }

        private void CompileConstructorBody(ClassDefinition classDef, HashSet<string>? fields)
        {
            var stmts = classDef.Statement.StatementsToExecute;
            if (stmts.Count == 0) return;

            // The class body (T3.4, 5.7): a bare read is GetGlobal, which the
            // VM looks up in the instance part's fields and then in the
            // globals; a bare assignment writes a field (ThisSetProperty)
            // unless the name is declared global (SetGlobal, which the VM
            // sends to the globals from a class body).
            var child = new Compiler(_script, functionDepth: 0);
            child._source = _source;
            child._current = new CompiledFunction(classDef.ClassDetails.Name + "#ctor", 0);
            child._current.Chunk.Source = _source;
            child._line = Line(classDef.Statement);
            child.SetColumn(classDef.Statement.Column);
            child._classBody = true;
            child._className = classDef.ClassDetails.Name;
            child._classFields = fields;
            child._constructorParameters = classDef.ClassDetails.Properties.ToArray();
            child._declaredGlobals = BodyNames.Collect(classDef.ClassDetails.Properties, classDef.Statement, _source, classBody: true).Globals;

            child.WarnUnreachable(classDef.Statement);
            foreach (var stmt in stmts)
                child.CompileStatement(stmt);

            child.EmitReturn(0);
            child._current.LocalCount = child._maxLocals;
            child._current.LocalNames = child._localNames.ToArray();
            child._current.Chunk.FinalizePropertyCaches();
            VerifyOutput(child._current);
            classDef.CompiledConstructor = child._current;
        }

        /// <summary>
        /// Runs <see cref="BytecodeVerifier"/> on a finished function, which
        /// computes the <see cref="CompiledFunction.MaxStack"/> the VM checks
        /// when it calls it (T1.7). A function needing more than the value
        /// stack is accepted and raises "Value stack overflow" when called; any
        /// other rejection is a compiler bug, reported as a host fault.
        /// </summary>
        private void VerifyOutput(CompiledFunction function)
        {
            // A compile that recovered from an error (T3.8) fails: what it
            // emitted is neither verified nor kept.
            if (_script.HasCompileErrors) return;
            // The limits nothing caught while emitting (T1.8): straight-line
            // code past 65,535 bytes, or more local slots than the file
            // records.
            var chunk = function.Chunk;
            if (chunk.Count > BytecodeVerifier.MaxCodeLength)
                throw new CompileError(Chunk.FunctionTooLarge, chunk.Source, LineNear(chunk, BytecodeVerifier.MaxCodeLength));
            if (function.LocalCount > 0xFFFF)
                throw new CompileError("too many local variables in one function (more than 65,535)", chunk.Source, LineNear(chunk, chunk.Count - 1));
            try { BytecodeVerifier.Verify(function, atLoad: false); }
            catch (System.IO.InvalidDataException e)
            {
                throw new System.InvalidOperationException("Compiler produced bytecode the verifier rejects: " + e.Message, e);
            }
        }

        /// <summary>
        /// The source line of the code at <paramref name="offset"/>, or of the
        /// nearest code before it that has one (only a function's implicit
        /// return has line 0, T1.10); 0 when none does.
        /// </summary>
        private static int LineNear(Chunk chunk, int offset)
        {
            for (int i = chunk.Lines.EntryIndex(offset); i >= 0; i--)
                if (chunk.Lines.LineAt(i) > 0) return chunk.Lines.LineAt(i);
            return 0;
        }

        /// <summary>
        /// Raises a <see cref="CompileError"/> for a function or lambda with
        /// more parameters than a call can pass (T1.8; the loader rejects an
        /// arity above 255 too).
        /// </summary>
        private static void CheckParameterCount(int count, string? source, int line, int column)
        {
            if (count > BytecodeVerifier.MaxArity)
                throw new CompileError("more than 255 parameters", source, line, column);
        }

        // ────────────────────────────────────────────────────────
        //  Lambda compilation
        // ────────────────────────────────────────────────────────

        private static CompiledFunction CompileLambda(WarScriptLanguage script, LambdaExpression lambda, string? source,
            HashSet<string>? enclosingLocals)
        {
            CheckParameterCount(lambda.Parameters.Count, source, lambda.Line, lambda.Column);
            var compiler = new Compiler(script, functionDepth: 1);
            compiler._source = source;
            compiler._current = new CompiledFunction("<lambda>", lambda.Parameters.Count);
            compiler._current.Chunk.Source = source;
            compiler._line = lambda.Line;
            compiler.SetColumn(lambda.Column);
            compiler._enclosingLocals = enclosingLocals;
            compiler.BeginBody(lambda.Parameters, lambda.Body);

            compiler.WarnUnreachable(lambda.Body);
            foreach (var stmt in lambda.Body.StatementsToExecute)
                compiler.CompileStatement(stmt);
            compiler.WarnUnreadLocals();

            compiler.EmitReturn(0);
            compiler._current.LocalCount = compiler._maxLocals;
            compiler._current.LocalNames = compiler._localNames.ToArray();
            compiler._current.Chunk.FinalizePropertyCaches();
            compiler.VerifyOutput(compiler._current);
            return compiler._current;
        }

        private CompiledFunction CompileLambdaBody(LambdaExpression lambda)
        {
            // The stack slots visible where the lambda is written: reading
            // one from inside it would need a closure (PROP-16).
            var enclosing = _enclosingLocals != null ? new HashSet<string>(_enclosingLocals) : new HashSet<string>();
            foreach (var local in _locals)
                if (local.Kind != LocalKind.Hidden)
                    enclosing.Add(local.Name);
            return CompileLambda(_script, lambda, _source, enclosing);
        }

        /// <summary>
        /// Starts a function, method or lambda body (T3.4): the parameters
        /// take the first slots, then every name the body assigns without
        /// declaring it <c>global</c> gets a slot of its own for the whole
        /// body, in the order of first assignment (PROP-9); the VM fills them
        /// with null when it pushes the frame (T3.5,
        /// <see cref="CompiledFunction.ReservedLocals"/>). Loop and rescue
        /// variables get theirs when their block starts. Reports the scoping errors <see cref="BodyNames"/> finds.
        /// </summary>
        private void BeginBody(IReadOnlyList<string> parameters, CompositeStatement body)
        {
            _resolveNames = true;
            BeginScope();
            foreach (var parameter in parameters)
                AddLocal(parameter, LocalKind.Parameter);

            var names = BodyNames.Collect(parameters, body, _source);
            _declaredGlobals = names.Globals;
            // The VM fills these slots with null when it pushes the frame
            // (T3.5): no code is emitted for them.
            foreach (var name in names.Locals)
                AddLocal(name, LocalKind.BodyLocal);
            _current.ReservedLocals = names.Locals.Count;
        }

        /// <summary>
        /// At the end of a function, method or lambda body: a warning for
        /// each body local the body assigns but never reads (PROP-26, T3.8),
        /// at its first assignment, in slot order. Under DEC-5 a typo on the
        /// left of an assignment makes a new local, which this catches.
        /// </summary>
        private void WarnUnreadLocals()
        {
            if (_recovered) return;
            foreach (var local in _locals)
            {
                if (local.Kind != LocalKind.BodyLocal || _readLocals.Contains(local.Name)) continue;
                var (line, column) = _firstAssignments.TryGetValue(local.Name, out var at) ? at : (_line, 0);
                _script.Warn("local '" + local.Name + "' is assigned but never read", _source, line, column);
            }
        }

        /// <summary>
        /// A warning for the first statement after a <c>return</c>,
        /// <c>break</c>, <c>next</c> or <c>raise</c> in the same block
        /// (PROP-29, T3.8), in <paramref name="block"/> and the blocks nested
        /// in it (not in nested definitions or lambdas, which are bodies of
        /// their own). One warning per block; statements without a line
        /// (desugared ones) are skipped.
        /// </summary>
        private void WarnUnreachable(CompositeStatement block)
        {
            string? after = null;
            foreach (var statement in block.StatementsToExecute)
            {
                if (after != null)
                {
                    if (statement.RowNumber is int row && row > 0)
                    {
                        _script.Warn("unreachable code after '" + after + "'", _source, row, statement.Column);
                        return;
                    }
                    continue;
                }
                switch (statement)
                {
                    case ReturnStatement:          after = "return"; break;
                    case BreakStatement:           after = "break";  break;
                    case NextStatement:            after = "next";   break;
                    case RaiseExceptionStatement:  after = "raise";  break;
                    case ConditionStatement c:
                        foreach (var branch in c.Cases) WarnUnreachable(branch.Value);
                        break;
                    case AbstractLoopStatement loop:
                        WarnUnreachable(loop);
                        break;
                    case HandleExceptionStatement h:
                        WarnUnreachable(h.BeginStatement);
                        if (h.RescueStatement != null) WarnUnreachable(h.RescueStatement);
                        if (h.EnsureStatement != null) WarnUnreachable(h.EnsureStatement);
                        break;
                }
            }
        }

        // ────────────────────────────────────────────────────────
        //  Local variable management
        // ────────────────────────────────────────────────────────

        private int ResolveLocal(string name)
        {
            for (int i = _locals.Count - 1; i >= 0; i--)
            {
                if (_locals[i].Name == name)
                    return i;
            }
            return -1;
        }

        private int AddLocal(string name) =>
            AddLocal(name, name.StartsWith("$", System.StringComparison.Ordinal) ? LocalKind.Hidden : LocalKind.Block);

        private int AddLocal(string name, LocalKind kind)
        {
            var slot = _locals.Count;
            _locals.Add(new Local { Name = name, Depth = _scopeDepth, Kind = kind });
            if (_locals.Count > _maxLocals)
                _maxLocals = _locals.Count;

            // Record debug name for this slot (first name wins if reused)
            while (_localNames.Count <= slot) _localNames.Add(null!);
            _localNames[slot] ??= name;

            return slot;
        }

        private void BeginScope() => _scopeDepth++;

        private void EndScope(int line)
        {
            _scopeDepth--;
            int popCount = 0;
            while (_locals.Count > 0 && _locals[^1].Depth > _scopeDepth)
            {
                _locals.RemoveAt(_locals.Count - 1);
                popCount++;
            }
            Chunk.EmitPops(popCount, line);
        }

        /// <summary>
        /// The argument count operand of a call or <c>new</c>: raises a
        /// <see cref="CompileError"/> above 255 (T1.8).
        /// </summary>
        private void EmitArgCount(int count, int line)
        {
            if (count > BytecodeVerifier.MaxArity)
                throw Chunk.LimitError("more than 255 arguments in a call");
            Chunk.EmitByte((byte)count, line);
        }

        private void EmitReturn(int line)
        {
            Chunk.EmitOp(OpCode.Null, line);
            Chunk.EmitOp(OpCode.Return, line);
        }

        /// <summary>
        /// Emit a conditional jump. Returns the patch offset.
        /// Note: compare+jump fusion is only used in direct-emission sites
        /// (for-loop, iterable-loop) where we control the full pattern.
        /// For if/while conditions that may contain logical AND/OR,
        /// we always emit JumpIfFalse to avoid corrupting jump targets.
        /// </summary>
        private int EmitConditionJump(int line)
        {
            return Chunk.EmitJump(OpCode.JumpIfFalse, line);
        }

        // Blocks have no runtime scope (T3.4, DEC-4; T3.5 removed the
        // PushScope/PopScope opcodes): a block's loop and rescue variables
        // are stack slots, and every other name is a local of the whole body,
        // a field (class body) or a global (top level).

        private static int Line(Statement.Statement stmt) => stmt.RowNumber ?? 0;

        // ────────────────────────────────────────────────────────
        //  Statement compilation
        // ────────────────────────────────────────────────────────

        private void CompileStatement(Statement.Statement stmt)
        {
            var outerLine = _line;
            var outerColumn = _column;
            if (Line(stmt) > 0)
            {
                _line = Line(stmt);
                SetColumn(stmt.Column);
            }
            var state = SaveState();
            try { CompileStatementBody(stmt); }
            catch (CompileError e) when (e.Line == 0 && _line > 0)
            {
                // A limit found while emitting (T1.8) carries no line: report
                // the innermost statement that has one.
                var located = new CompileError(e.Reason, e.ScriptName, _line, _column);
                if (!_script.RecoverFromCompileError(located)) throw located;
                Recover(stmt, state);
            }
            catch (SyntaxException e) when (!(e is CompileError))
            {
                // Likewise for the compiler's other errors (T3.1).
                e.Locate(_line, _column).LocateIn(_source, null);
                if (!_script.RecoverFromCompileError(e)) throw;
                Recover(stmt, state);
            }
            catch (CompileError e) when (_script.RecoverFromCompileError(e))
            {
                Recover(stmt, state);
            }
            finally
            {
                _line = outerLine;
                SetColumn(outerColumn);
            }
        }

        /// <summary>What <see cref="Recover"/> puts back: the compiler's block state where a statement starts.</summary>
        private readonly struct StatementState
        {
            public readonly int Locals, ScopeDepth, Loops, Tries, TryDepth, InlinedEnsure;

            public StatementState(int locals, int scopeDepth, int loops, int tries, int tryDepth, int inlinedEnsure)
            {
                Locals = locals;
                ScopeDepth = scopeDepth;
                Loops = loops;
                Tries = tries;
                TryDepth = tryDepth;
                InlinedEnsure = inlinedEnsure;
            }
        }

        private StatementState SaveState() =>
            new StatementState(_locals.Count, _scopeDepth, _loops.Count, _tries.Count, _tryDepth, _inlinedEnsure);

        /// <summary>
        /// Continues after a compile error in <paramref name="stmt"/>, which
        /// <see cref="WarScriptLanguage.RecoverFromCompileError"/> recorded
        /// (T3.8: several errors per compile): the blocks, loops and begin
        /// blocks the statement had opened are closed again, as at its start
        /// (the code emitted so far is never run or verified, the compile
        /// fails), and every name it assigns counts as assigned, so that a
        /// later read is not reported as read before its assignment or as an
        /// undefined global because of this error. The body reports no
        /// unread locals (<see cref="WarnUnreadLocals"/>): a read may have
        /// been in the code not compiled.
        /// </summary>
        private void Recover(Statement.Statement stmt, StatementState state)
        {
            _locals.RemoveRange(state.Locals, _locals.Count - state.Locals);
            _scopeDepth = state.ScopeDepth;
            _loops.RemoveRange(state.Loops, _loops.Count - state.Loops);
            _tries.RemoveRange(state.Tries, _tries.Count - state.Tries);
            _tryDepth = state.TryDepth;
            _inlinedEnsure = state.InlinedEnsure;
            _recovered = true;
            foreach (var name in BodyNames.Assigned(stmt))
            {
                _usedNames.Add(name);
                var slot = ResolveLocal(name);
                if (slot != -1)
                {
                    if (_locals[slot].Kind == LocalKind.BodyLocal) _assignedSoFar.Add(name);
                }
                else if (!_classBody || _declaredGlobals.Contains(name))
                    _script.DeclareGlobalWrite(name);
            }
        }

        private void CompileStatementBody(Statement.Statement stmt)
        {
            switch (stmt)
            {
                case ExpressionStatement s:   CompileExpressionStatement(s); break;
                case PrintStatement s:        CompilePrint(s);              break;
                case ReturnStatement s:       CompileReturn(s);             break;
                case AssertStatement s:       CompileAssert(s);             break;
                case ConditionStatement s:    CompileCondition(s);          break;
                case ForLoopStatement s:      CompileForLoop(s);            break;
                case IterableLoopStatement s: CompileIterableLoop(s);       break;
                case WhileLoopStatement s:    CompileWhileLoop(s);          break;
                case BreakStatement s:        CompileBreak(s);              break;
                case NextStatement s:         CompileNext(s);               break;
                case RaiseExceptionStatement s:  CompileRaise(s);           break;
                case HandleExceptionStatement s: CompileTryRescueEnsure(s); break;
                // An import compiles to nothing: the VM runs the imports'
                // top-level code before the script's (5.8, T3.7).
                case ImportStatement _:                                     break;
                case YieldStatement s:        CompileYield(s);              break;
                case GlobalStatement s:       CompileGlobal(s);             break;
                // FunctionStatement and ClassStatement are handled via definition compilation
            }
        }

        // ── Expression statement ──

        private void CompileExpressionStatement(ExpressionStatement stmt)
        {
            int line = Line(stmt);

            // Special-case: simple variable assignment (may be a local declaration)
            if (stmt.Expression is AssignmentOperator assign)
            {
                bool newLocal = CompileAssignment(assign, line);
                if (!newLocal)
                    Chunk.EmitOp(OpCode.Pop, line);
                return;
            }

            CompileExpression(stmt.Expression);
            Chunk.EmitOp(OpCode.Pop, line);
        }

        /// <summary>
        /// Compile an assignment. Returns true if a new local was declared
        /// (caller should NOT emit Pop because the value on TOS IS the local).
        /// </summary>
        private bool CompileAssignment(AssignmentOperator assign, int line)
        {
            // ── Variable assignment: x = expr ──
            if (assign.Left is VariableExpression v)
            {
                CompileExpression(assign.Right);
                _usedNames.Add(v.Name);
                // Any bare assignment of a constant's name is a reassignment
                // (T3.8), whatever it compiles to: the parser rejects a body
                // local or a class-body field named like a constant it has
                // already read, so one above the declaration is reported too.
                // Not the parser's own desugared writes (a default parameter
                // value, an enum's members): they write a parameter or a
                // field of the enum's class, never the constant.
                if (!assign.DeclaresConstant && !assign.Desugared)
                {
                    var onThisLine = v.Line > 0 && v.Line == _line;
                    _script.CheckConstantWrite(v.Name, _source,
                        onThisLine ? v.Line : _line, onThisLine ? v.Column : _column);
                }
                var slot = ResolveLocal(v.Name);
                if (slot != -1)
                {
                    Chunk.EmitOp(OpCode.SetLocal, line);
                    Chunk.EmitU16(slot, line);
                    if (_locals[slot].Kind == LocalKind.BodyLocal && _assignedSoFar.Add(v.Name))
                        _firstAssignments[v.Name] = v.Line > 0 && v.Line == _line ? (v.Line, v.Column) : (_line, _column);
                    return false;
                }
                // In a class body a bare assignment declares (or writes) a
                // field of the instance part, unless the name is declared
                // global (5.7, PROP-10).
                if (_classBody && !_declaredGlobals.Contains(v.Name))
                {
                    var fieldIdx = Chunk.AddConstant(WarValue.FromText(v.Name));
                    Chunk.EmitOp(OpCode.ThisSetProperty, line);
                    Chunk.EmitU16(fieldIdx, line);
                    Chunk.EmitU16(Chunk.AllocCacheSlot(), line);
                    return false;
                }
                // Not a stack slot or a field: a global. In a function,
                // method, lambda or class body that is a name declared
                // `global` (T3.4), and the VM writes the instance globals,
                // creating the name if needed. At the top level SetGlobal
                // writes the scope chain (the globals, then the host
                // globals), creating the name in the globals.
                _script.DeclareGlobalWrite(v.Name);
                if (assign.DeclaresConstant && _script.CollectsTypedOperands)
                    _script.DeclareConstantType(v.Name, ConstantType(assign.Right));
                var nameIdx = Chunk.AddConstant(WarValue.FromText(v.Name));
                Chunk.EmitOp(OpCode.SetGlobal, line);
                Chunk.EmitU16(nameIdx, line);
                return false;
            }

            // ── Array index assignment: arr{i} = expr ──
            if (assign.Left is ArrayValueOperator av)
            {
                // If the target is a simple variable, use IndexSetLocal/Global
                // (they read the variable; text is immutable since T3.6)
                if (av.Left is VariableExpression avVar)
                {
                    CompileExpression(av.Right);      // index
                    CompileExpression(assign.Right);  // value
                    var slot = ResolveRead(avVar);
                    if (slot != -1)
                    {
                        Chunk.EmitOp(OpCode.IndexSetLocal, line);
                        Chunk.EmitU16(slot, line);
                    }
                    else
                    {
                        CheckGlobalRead(avVar);
                        var nameIdx = Chunk.AddConstant(WarValue.FromText(avVar.Name));
                        Chunk.EmitOp(OpCode.IndexSetGlobal, line);
                        Chunk.EmitU16(nameIdx, line);
                    }
                    return false;
                }

                // Generic case: target is an expression (e.g. obj::prop{i} = val)
                CompileExpression(av.Left);       // target
                CompileExpression(av.Right);      // index
                CompileExpression(assign.Right);  // value
                Chunk.EmitOp(OpCode.IndexSet, line);
                return false;
            }

            // ── Property assignment: obj::prop = expr ──
            if (assign.Left is ClassPropertyOperator cp)
            {
                if (cp.Right is VariableExpression propName)
                {
                    // Superinstruction: this :: prop = val → ThisSetProperty
                    if (cp.Left is ThisExpression)
                    {
                        CheckField(propName.Name, propName.Line, propName.Column);
                        CompileExpression(assign.Right);  // value
                        var nameIdx = Chunk.AddConstant(WarValue.FromText(propName.Name));
                        Chunk.EmitOp(OpCode.ThisSetProperty, line);
                        Chunk.EmitU16(nameIdx, line);
                        Chunk.EmitU16(Chunk.AllocCacheSlot(), line);
                        return false;
                    }

                    CompileExpression(cp.Left);      // instance
                    CheckInstanceOperand(cp.Left, "Cannot set property '" + propName.Name + "' on non-class value");
                    CheckProperty(cp.Left, propName.Name, propName.Line, propName.Column);
                    CompileExpression(assign.Right);  // value
                    var nameIdx2 = Chunk.AddConstant(WarValue.FromText(propName.Name));
                    Chunk.EmitOp(OpCode.SetProperty, line);
                    Chunk.EmitU16(nameIdx2, line);
                    Chunk.EmitU16(Chunk.AllocCacheSlot(), line);
                    return false;
                }
                // obj::arr{i} = expr  →  IndexSetProp
                if (cp.Right is ArrayValueOperator propArr && propArr.Left is VariableExpression arrName)
                {
                    if (cp.Left is ThisExpression) CheckField(arrName.Name, arrName.Line, arrName.Column);
                    CompileExpression(cp.Left);         // instance
                    CheckInstanceOperand(cp.Left, "Cannot access property '" + arrName.Name + "' on non-class value");
                    if (!(cp.Left is ThisExpression)) CheckProperty(cp.Left, arrName.Name, arrName.Line, arrName.Column);
                    CompileExpression(propArr.Right);    // index
                    CompileExpression(assign.Right);     // value
                    var nameIdx = Chunk.AddConstant(WarValue.FromText(arrName.Name));
                    Chunk.EmitOp(OpCode.IndexSetProp, line);
                    Chunk.EmitU16(nameIdx, line);
                    Chunk.EmitU16(Chunk.AllocCacheSlot(), line);
                    return false;
                }
            }

            // Anything else is not assignable. CompileExpression would dispatch
            // right back here, so recursing on the same node can only overflow
            // the stack — reject the assignment target instead.
            throw new SyntaxException(
                System.FormattableString.Invariant($"Invalid assignment target at line {line}: ") +
                "only variables, array elements and class properties can be assigned to");
        }

        // ── Print ──
        private void CompilePrint(PrintStatement stmt)
        {
            CompileExpression(stmt.Expression);
            Chunk.EmitOp(OpCode.Print, Line(stmt));
        }

        // ── Return ──
        private void CompileReturn(ReturnStatement stmt)
        {
            // Tail call optimization: when a function's last action is
            // "return func[args]", emit TailCall instead of Call + Return.
            // This reuses the current frame, preventing stack growth on
            // deep recursion (state machines, tree traversal, mutual recursion).
            //
            // Conditions for TCO:
            //   1. Inside a function (not top-level script)
            //   2. Not inside a begin/rescue/ensure block (would bypass ensure)
            //   3. The return expression is a plain function call (not a method call)
            //   4. The call target is not a local variable (TailCall uses name-based
            //      lookup which can't find stack locals holding lambda values)
            if (_functionDepth > 0
                && _tryDepth == 0
                && stmt.Expression is FunctionExpression tailCall
                && ResolveLocal(tailCall.Name) == -1)
            {
                int line = Line(stmt);
                foreach (var arg in tailCall.ArgumentExpression)
                    CompileExpression(arg);
                CheckCall(tailCall);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(tailCall.Name));
                Chunk.EmitOp(OpCode.TailCall, line);
                Chunk.EmitU16(nameIdx, line);
                EmitArgCount(tailCall.ArgumentExpression.Count, line);
                return;
            }

            CompileExpression(stmt.Expression);
            Chunk.EmitOp(OpCode.Return, Line(stmt));
        }

        // ── Assert ──
        private void CompileAssert(AssertStatement stmt)
        {
            CompileExpression(stmt.Expression);
            Chunk.EmitOp(OpCode.Assert, Line(stmt));
        }

        // ── If / Elif / Else ──
        private void CompileCondition(ConditionStatement stmt)
        {
            int line = Line(stmt);
            var endJumps = new List<int>();

            for (int i = 0; i < stmt.Cases.Count; i++)
            {
                var cond = stmt.Cases[i].Key;
                var body = stmt.Cases[i].Value;

                bool isElse = cond is ConstantExpression ce && ce.Value.IsLogical && ce.Value.LogicalValue;

                int skipBody = -1;
                if (!isElse)
                {
                    CompileExpression(cond);
                    skipBody = EmitConditionJump(line);
                    Chunk.EmitOp(OpCode.Pop, line); // pop condition (truthy path)
                }

                BeginScope();
                foreach (var s in body.StatementsToExecute)
                    CompileStatement(s);
                EndScope(line);

                // After the body, jump to the very end (past all false-path Pops)
                endJumps.Add(Chunk.EmitJump(OpCode.Jump, line));

                // Patch false-path landing: JumpIfFalse lands here
                if (skipBody != -1)
                {
                    Chunk.PatchJump(skipBody);
                    Chunk.EmitOp(OpCode.Pop, line); // pop condition (falsy path)
                }
            }

            // All end-of-body jumps land here
            foreach (var j in endJumps)
                Chunk.PatchJump(j);
        }

        // ── While loop ──
        private void CompileWhileLoop(WhileLoopStatement stmt)
        {
            int line = Line(stmt);
            var loopStart = Chunk.Count;

            _loops.Add(new LoopCtx
            {
                LoopStart = loopStart,
                ContinueTarget = loopStart,
                BreakJumps = new List<int>(),
                NextJumps = new List<int>(),
                UsesForwardNext = false,
                ScopeDepth = _scopeDepth,
                LocalCountAtLoop = _locals.Count
            });

            CompileExpression(stmt.Condition);
            var exitJump = EmitConditionJump(line);
            Chunk.EmitOp(OpCode.Pop, line);

            BeginScope();
            foreach (var s in stmt.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);

            Chunk.EmitLoop(loopStart, line);

            Chunk.PatchJump(exitJump);
            Chunk.EmitOp(OpCode.Pop, line);

            var loop = _loops[_loops.Count - 1]; _loops.RemoveAt(_loops.Count - 1);
            foreach (var bj in loop.BreakJumps)
                Chunk.PatchJump(bj);
        }

        // ── For loop (loop i in low..high by step) ──
        private void CompileForLoop(ForLoopStatement stmt)
        {
            int line = Line(stmt);
            BeginScope();

            // Initialize counter
            CompileExpression(stmt.LowerBound);
            var iterSlot = AddLocal(stmt.Variable.Name);

            // Evaluate upper bound once, store as hidden local
            CompileExpression(stmt.UpperBound);
            var limitSlot = AddLocal("$limit");

            // Evaluate step once, store as hidden local
            CompileExpression(stmt.Step);
            var stepSlot = AddLocal("$step");

            // The first `i < limit` always runs (PROP-28, T3.8): bounds of
            // known types it rejects are a compile error, at the `..` (where
            // the VM reports it too). The step is only added once an
            // iteration completes, so it is never checked.
            if (stmt.RangeColumn > 0) SetColumn(stmt.RangeColumn);
            CheckOperands("<", stmt.LowerBound, stmt.UpperBound);

            var loopStart = Chunk.Count;

            _loops.Add(new LoopCtx
            {
                LoopStart = loopStart,
                ContinueTarget = 0,
                BreakJumps = new List<int>(),
                NextJumps = new List<int>(),
                UsesForwardNext = true,
                ScopeDepth = _scopeDepth,
                LocalCountAtLoop = _locals.Count
            });

            // Condition: i < limit (fused: LessJump = Less + JumpIfFalse + Pop)
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(iterSlot, line);
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(limitSlot, line);
            var exitJump = Chunk.EmitJump(OpCode.LessJump, line);

            // Body
            BeginScope();
            foreach (var s in stmt.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);

            // Patch ContinueTarget and NextJumps to point here (the increment)
            var incrementStart = Chunk.Count;
            var ctx = _loops[_loops.Count - 1];
            foreach (var nj in ctx.NextJumps)
                Chunk.PatchJump(nj);

            // Increment: i = i + step
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(iterSlot, line);
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(stepSlot, line);
            Chunk.EmitOp(OpCode.Add, line);
            Chunk.EmitOp(OpCode.SetLocal, line); Chunk.EmitU16(iterSlot, line);
            Chunk.EmitOp(OpCode.Pop, line);

            Chunk.EmitLoop(loopStart, line);

            Chunk.PatchJump(exitJump);
            // No Pop needed — LessJump consumed both operands

            var loop = _loops[_loops.Count - 1]; _loops.RemoveAt(_loops.Count - 1);
            foreach (var bj in loop.BreakJumps)
                Chunk.PatchJump(bj);

            EndScope(line); // pops i, $limit, $step
        }

        // ── Iterable loop (loop x in collection) ──
        private void CompileIterableLoop(IterableLoopStatement stmt)
        {
            int line = Line(stmt);
            BeginScope();

            // Evaluate iterable, normalize to array
            CompileExpression(stmt.Iterable);
            Chunk.EmitOp(OpCode.IterPrepare, line);
            var iterSlot = AddLocal("$items");

            // Index counter = 0
            Chunk.EmitConstant(WarValue.FromNumeric(0), line);
            var idxSlot = AddLocal("$idx");

            var loopStart = Chunk.Count;

            _loops.Add(new LoopCtx
            {
                LoopStart = loopStart,
                ContinueTarget = 0,
                BreakJumps = new List<int>(),
                NextJumps = new List<int>(),
                UsesForwardNext = true,
                ScopeDepth = _scopeDepth,
                LocalCountAtLoop = _locals.Count
            });

            // Condition: $idx < len($items) (fused: LessJump)
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(idxSlot, line);
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(iterSlot, line);
            Chunk.EmitOp(OpCode.Len, line);
            var exitJump = Chunk.EmitJump(OpCode.LessJump, line);

            // Set iteration variable: x = $items{$idx}
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(iterSlot, line);
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(idxSlot, line);
            Chunk.EmitOp(OpCode.IndexGet, line);
            var varSlot = AddLocal(stmt.Variable.Name);

            // Body
            BeginScope();
            foreach (var s in stmt.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);

            // Pop the iteration variable (it was added as a local above)
            _locals.RemoveAt(_locals.Count - 1);
            Chunk.EmitOp(OpCode.Pop, line);

            // Patch NextJumps: 'next' pops iter var via PopN, jumps here
            var incrementStart = Chunk.Count;
            var ctx = _loops[_loops.Count - 1];
            foreach (var nj in ctx.NextJumps)
                Chunk.PatchJump(nj);

            // Increment: $idx = $idx + 1
            Chunk.EmitOp(OpCode.GetLocal, line); Chunk.EmitU16(idxSlot, line);
            Chunk.EmitConstant(WarValue.FromNumeric(1), line);
            Chunk.EmitOp(OpCode.Add, line);
            Chunk.EmitOp(OpCode.SetLocal, line); Chunk.EmitU16(idxSlot, line);
            Chunk.EmitOp(OpCode.Pop, line);

            Chunk.EmitLoop(loopStart, line);

            Chunk.PatchJump(exitJump);
            // No Pop needed — LessJump consumed both operands

            var loop = _loops[_loops.Count - 1]; _loops.RemoveAt(_loops.Count - 1);
            foreach (var bj in loop.BreakJumps)
                Chunk.PatchJump(bj);

            EndScope(line); // pops $items, $idx
        }

        // ── Break ──
        private void CompileBreak(BreakStatement stmt)
        {
            if (_loops.Count == 0)
                throw new SyntaxException(System.FormattableString.Invariant($"'break' outside of a loop at line {Line(stmt)}"));
            int line = Line(stmt);

            var loop = _loops[_loops.Count - 1];
            var saved = SaveBlockState();
            EmitLeaveLoopBody(loop, line);
            loop.BreakJumps.Add(Chunk.EmitJump(OpCode.Jump, line));
            RestoreBlockState(saved);
        }

        // ── Next (continue) ──
        private void CompileNext(NextStatement stmt)
        {
            if (_loops.Count == 0)
                throw new SyntaxException(System.FormattableString.Invariant($"'next' outside of a loop at line {Line(stmt)}"));
            int line = Line(stmt);

            var loop = _loops[_loops.Count - 1];
            var saved = SaveBlockState();
            EmitLeaveLoopBody(loop, line);
            if (loop.UsesForwardNext)
            {
                // Forward jump — patched after body to point at increment
                loop.NextJumps.Add(Chunk.EmitJump(OpCode.Jump, line));
            }
            else
            {
                // While loop: backward jump to condition
                Chunk.EmitLoop(loop.ContinueTarget, line);
            }
            RestoreBlockState(saved);
        }

        private struct BlockState
        {
            public Local[] Locals;
            public int ScopeDepth;
            public TryCtx[] Tries;
        }

        // 'break' and 'next' emit the code that leaves the blocks they are
        // in, but the statements after them (unreachable) are compiled as if
        // still inside: the compile-time state is put back afterwards.
        private BlockState SaveBlockState() => new BlockState
        {
            Locals = _locals.ToArray(),
            ScopeDepth = _scopeDepth,
            Tries = _tries.ToArray()
        };

        private void RestoreBlockState(in BlockState state)
        {
            _locals.Clear();
            _locals.AddRange(state.Locals);
            _scopeDepth = state.ScopeDepth;
            _tries.Clear();
            _tries.AddRange(state.Tries);
        }

        /// <summary>
        /// Emits what leaving the body of <paramref name="loop"/> takes: for
        /// every begin block opened inside the loop, innermost first, pop its
        /// scopes and locals, then remove its handler (from the begin body, or
        /// the ensure-only one from the rescue body, Q-14) or discard its
        /// pending re-raise or return (from the ensure body), and
        /// run its ensure block unless already in it (Q-12, CRASH-8); finally
        /// pop the scopes and locals opened since the loop started.
        /// Changes the compile-time state; see <see cref="SaveBlockState"/>.
        /// </summary>
        private void EmitLeaveLoopBody(LoopCtx loop, int line)
        {
            while (_tries.Count > 0 && _tries[_tries.Count - 1].LoopCount == _loops.Count)
            {
                var t = _tries[_tries.Count - 1];
                _tries.RemoveAt(_tries.Count - 1);
                EmitUnwindTo(t.LocalCount, line);
                switch (t.Region)
                {
                    case TryRegion.Begin:
                        Chunk.EmitOp(OpCode.PopHandler, line);
                        break;
                    case TryRegion.Rescue:
                        // The ensure-only handler the rescue body runs under
                        // when the block has an ensure block (Q-14).
                        if (t.Ensure != null)
                            Chunk.EmitOp(OpCode.PopHandler, line);
                        break;
                    case TryRegion.Ensure:
                        Chunk.EmitOp(OpCode.DropPending, line);
                        t.DropPendingPatches.Add(Chunk.Count);
                        Chunk.EmitU16(0, line); // the block's end IP, patched with PushHandler's
                        break;
                }
                if (t.Region != TryRegion.Ensure && t.Ensure != null)
                {
                    _inlinedEnsure++;
                    try { CompileEnsureBody(t.Ensure, line); }
                    finally { _inlinedEnsure--; }
                }
            }
            EmitUnwindTo(loop.LocalCountAtLoop, line);
        }

        /// <summary>Pops stack locals down to the given count.</summary>
        private void EmitUnwindTo(int localCount, int line)
        {
            var popCount = _locals.Count - localCount;
            if (popCount > 0)
            {
                Chunk.EmitPops(popCount, line);
                _locals.RemoveRange(localCount, popCount);
            }
        }

        // ── Raise ──
        private void CompileRaise(RaiseExceptionStatement stmt)
        {
            CompileExpression(stmt.Expression);
            Chunk.EmitOp(OpCode.Raise, Line(stmt));
        }

        // ── Begin / Rescue / Ensure ──
        private void CompileTryRescueEnsure(HandleExceptionStatement stmt)
        {
            int line = Line(stmt);
            _tryDepth++;
            var tryCtx = new TryCtx
            {
                LoopCount = _loops.Count,
                LocalCount = _locals.Count,
                Region = TryRegion.Begin,
                Ensure = stmt.EnsureStatement
            };
            _tries.Add(tryCtx);

            // Emit PushHandler with placeholder offsets
            Chunk.EmitOp(OpCode.PushHandler, line);
            int patchBase = Chunk.Count;
            Chunk.EmitU16(0, line); // rescue offset placeholder
            Chunk.EmitU16(0, line); // ensure offset placeholder
            Chunk.EmitU16(0, line); // end offset placeholder
            Chunk.EmitByte((byte)(stmt.RescueStatement != null ? 1 : 0), line); // hasRescue flag

            // ── Try body ──
            BeginScope();
            foreach (var s in stmt.BeginStatement.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);

            // Normal exit: pop handler, fall through to ensure
            tryCtx.Region = TryRegion.Rescue;
            Chunk.EmitOp(OpCode.PopHandler, line);
            var jumpToEnsure = Chunk.EmitJump(OpCode.Jump, line);

            // ── Rescue body ──
            int rescueIP = Chunk.Count;
            if (stmt.RescueStatement != null)
            {
                BeginScope();
                // The VM always pushes the error value before jumping here.
                if (stmt.ErrorVariable != null)
                    AddLocal(stmt.ErrorVariable);
                else
                    AddLocal("$error");
                foreach (var s in stmt.RescueStatement.StatementsToExecute)
                    CompileStatement(s);
                EndScope(line);
            }
            else
            {
                // No rescue block: never entered (the VM goes straight to the
                // ensure block), but kept valid for the verifier.
                Chunk.EmitOp(OpCode.Pop, line);
            }
            // With an ensure block, the VM runs the rescue body under an
            // ensure-only handler, so that leaving it by return, raise or an
            // error runs the ensure block (Q-14, SEM-13); its normal end
            // removes that handler and falls through to the ensure block.
            // Without one there is no such handler (BytecodeVerifier).
            if (stmt.EnsureStatement != null)
                Chunk.EmitOp(OpCode.PopHandler, line);

            // ── Ensure body ──
            int ensureIP = Chunk.Count;
            Chunk.PatchJump(jumpToEnsure);
            tryCtx.Region = TryRegion.Ensure;
            if (stmt.EnsureStatement != null)
                CompileEnsureBody(stmt.EnsureStatement, line);

            int endIP = Chunk.Count;
            _tries.RemoveAt(_tries.Count - 1);
            foreach (var at in tryCtx.DropPendingPatches)
                Chunk.PatchAddress(at, endIP);

            // Patch the PushHandler operands (absolute IPs)
            Chunk.PatchAddress(patchBase, rescueIP);
            Chunk.PatchAddress(patchBase + 2, ensureIP);
            Chunk.PatchAddress(patchBase + 4, endIP);

            _tryDepth--;
        }

        private void CompileEnsureBody(CompositeStatement ensure, int line)
        {
            BeginScope();
            foreach (var s in ensure.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);
        }

        // ── Global declaration (T3.4, PROP-10) ──
        private void CompileGlobal(GlobalStatement stmt)
        {
            // The declaration itself emits nothing: the pre-pass gave the
            // body's writes of the name their meaning (SetGlobal).
            if (!_resolveNames && !_classBody) return;
            foreach (var name in stmt.Names)
            {
                var slot = ResolveLocal(name);
                if (slot != -1 && _locals[slot].Kind == LocalKind.Parameter)
                    throw new CompileError("'" + name + "' is a parameter and cannot be declared global", _source, _line, _column);
                if (_classBody && IsConstructorParameter(name))
                    throw new CompileError("'" + name + "' is a constructor parameter and cannot be declared global", _source, _line, _column);
                if (_usedNames.Contains(name))
                    throw new CompileError("'global " + name + "' must come before any use of '" + name + "' in this body", _source, _line, _column);
            }
        }

        private string[]? _constructorParameters;

        private bool IsConstructorParameter(string name) =>
            _constructorParameters != null && System.Array.IndexOf(_constructorParameters, name) >= 0;

        /// <summary>
        /// <c>@x</c> / <c>this :: x</c> in a method or class body must name a
        /// field of the class or its bases (5.7, DEC-6): a compile error
        /// otherwise. Not checked when the fields are not known
        /// (<see cref="_classFields"/>).
        /// </summary>
        private void CheckField(string name, int line, int column)
        {
            if (_classFields == null || _classFields.Contains(name)) return;
            if (line <= 0 || line != _line) { line = _line; column = _column; }
            throw new CompileError("'" + name + "' is not a field of class '" + _className + "'", _source, line, column);
        }

        /// <summary>
        /// <c>obj :: name</c> and <c>obj :: name [...]</c> must name a field or
        /// a method of some class, and <c>E :: X</c> a member of the enum
        /// <c>E</c> (T3.8, PROP-29): recorded for
        /// <see cref="WarScriptLanguage.CheckProperty"/>, checked once the
        /// module set is compiled. <paramref name="left"/> names an enum only
        /// when it is a bare name read as a global (not a local, and not a
        /// field of the class body being compiled).
        /// </summary>
        private void CheckProperty(IExpression left, string name, int line, int column)
        {
            if (line <= 0 || line != _line) { line = _line; column = _column; }
            string? enumName = null;
            if (left is VariableExpression v && ResolveLocal(v.Name) == -1
                && !(_classBody && (_classFields == null || _classFields.Contains(v.Name))))
                enumName = v.Name;
            _script.CheckProperty(name, enumName, _source, line, column);
        }

        /// <summary>
        /// A bare read of a global must name one the module set writes or the
        /// host defines (T3.8, PROP-27): recorded for
        /// <see cref="WarScriptLanguage.CheckGlobalRead"/>, checked once the
        /// module set is compiled. In a class body a field of the class is
        /// found first, so a known field (or unknown fields: a base class the
        /// compiler does not see) is not recorded.
        /// </summary>
        private void CheckGlobalRead(VariableExpression v)
        {
            if (_classBody && (_classFields == null || _classFields.Contains(v.Name))) return;
            var onThisLine = v.Line > 0 && v.Line == _line;
            _script.CheckGlobalRead(v.Name, _classBody, _source,
                onThisLine ? v.Line : _line, onThisLine ? v.Column : _column);
        }

        /// <summary>
        /// The slot a read of <paramref name="v"/> uses, or -1 for a global
        /// read, after the read checks of <see cref="CheckRead"/>.
        /// </summary>
        private int ResolveRead(VariableExpression v)
        {
            var slot = ResolveLocal(v.Name);
            CheckRead(v.Name, slot, v.Line, v.Column);
            return slot;
        }

        /// <summary>
        /// A read of a body local before its first assignment in the source
        /// is a compile error (PROP-9), and so is a lambda's read of a stack
        /// slot of the code around it (PROP-16, no closures).
        /// </summary>
        private void CheckRead(string name, int slot, int line, int column)
        {
            _usedNames.Add(name);
            if (line <= 0 || line != _line) { line = _line; column = _column; }
            if (slot != -1)
            {
                if (_locals[slot].Kind == LocalKind.BodyLocal)
                {
                    if (_inlinedEnsure == 0 && !_assignedSoFar.Contains(name))
                        throw new CompileError("variable '" + name + "' is read before it is assigned", _source, line, column);
                    _readLocals.Add(name);
                }
                return;
            }
            if (_enclosingLocals != null && _enclosingLocals.Contains(name) && !_declaredGlobals.Contains(name))
                throw new CompileError("closures are not supported: '" + name + "' is a local variable of the code around the lambda", _source, line, column);
        }

        // ── Yield ──
        private void CompileYield(YieldStatement stmt)
        {
            int line = Line(stmt);
            switch (stmt.YieldType)
            {
                case YieldType.NextTick:
                    Chunk.EmitOp(OpCode.Yield, line);
                    break;
                case YieldType.Wait:
                    CompileExpression(stmt.Expression!);
                    Chunk.EmitOp(OpCode.YieldWait, line);
                    break;
                case YieldType.Until:
                    // Compile as a yield-loop: check condition, if false yield
                    // and re-check on resume. Each tick costs ~6 instructions.
                    //   loop_start:
                    //     <condition>
                    //     JumpIfTrue after_yield
                    //     Pop (false)
                    //     OP_Yield
                    //     Jump loop_start
                    //   after_yield:
                    //     Pop (true)
                    var loopStart = Chunk.Count;
                    CompileExpression(stmt.Expression!);
                    var exitJump = Chunk.EmitJump(OpCode.JumpIfTrue, line);
                    Chunk.EmitOp(OpCode.Pop, line);
                    Chunk.EmitOp(OpCode.Yield, line);
                    Chunk.EmitLoop(loopStart, line);
                    Chunk.PatchJump(exitJump);
                    Chunk.EmitOp(OpCode.Pop, line);
                    break;
            }
        }

        // ────────────────────────────────────────────────────────
        //  Expression compilation
        // ────────────────────────────────────────────────────────

        private void CompileExpression(IExpression expr)
        {
            // An expression positioned on the line being compiled moves the
            // column there (T3.1), for its instructions and its errors. Not
            // restored when it throws: the statement reports the innermost.
            if (expr is IPositionedExpression positioned && positioned.Column > 0
                && positioned.Line == _line && positioned.Column != _column)
            {
                var outerColumn = _column;
                SetColumn(positioned.Column);
                CompileExpressionAt(expr);
                SetColumn(outerColumn);
                return;
            }
            CompileExpressionAt(expr);
        }

        private void CompileExpressionAt(IExpression expr)
        {
            switch (expr)
            {
                // ── Lambda (anonymous function) ──
                case LambdaExpression lambda:
                {
                    var compiled = CompileLambdaBody(lambda);
                    // Owned by the instance compiling it (6.7, Q-1).
                    var funcValue = WarValue.FromNativeObject(new FunctionValue(compiled, _script));
                    Chunk.EmitConstant(funcValue, lambda.Line);
                    break;
                }

                // ── Literals ──
                case ConstantExpression c:
                {
                    var val = c.Value;
                    if (val.IsNull) Chunk.EmitOp(OpCode.Null, _line);
                    else if (val.IsLogical) Chunk.EmitOp(val.LogicalValue ? OpCode.True : OpCode.False, _line);
                    else Chunk.EmitConstant(val, _line);
                    break;
                }

                case ThisExpression:
                    Chunk.EmitOp(OpCode.This, _line);
                    break;

                // ── Variables ──
                case VariableExpression v:
                {
                    var slot = ResolveRead(v);
                    if (slot != -1)
                    {
                        Chunk.EmitOp(OpCode.GetLocal, _line);
                        Chunk.EmitU16(slot, _line);
                    }
                    else
                    {
                        CheckGlobalRead(v);
                        Chunk.EmitOp(OpCode.GetGlobal, _line);
                        Chunk.EmitU16(Chunk.AddConstant(WarValue.FromText(v.Name)), _line);
                    }
                    break;
                }

                // ── Assignment (as sub-expression) ──
                case AssignmentOperator a:
                    CompileAssignment(a, _line);
                    break;

                // ── Arithmetic ──
                case AdditionOperator a:
                    CompileExpression(a.Left);
                    CompileExpression(a.Right);
                    CheckOperands("+", a.Left, a.Right);
                    Chunk.EmitOp(OpCode.Add, _line);
                    break;

                case SubtractionOperator s:
                    CompileExpression(s.Left);
                    CompileExpression(s.Right);
                    CheckOperands("-", s.Left, s.Right);
                    Chunk.EmitOp(OpCode.Sub, _line);
                    break;

                case MultiplicationOperator m:
                    CompileExpression(m.Left);
                    CompileExpression(m.Right);
                    CheckOperands("*", m.Left, m.Right);
                    Chunk.EmitOp(OpCode.Mul, _line);
                    break;

                case DivisionOperator d:
                    CompileExpression(d.Left);
                    CompileExpression(d.Right);
                    CheckOperands("/", d.Left, d.Right);
                    Chunk.EmitOp(OpCode.Div, _line);
                    break;

                case ModuloOperator mo:
                    CompileExpression(mo.Left);
                    CompileExpression(mo.Right);
                    CheckOperands("%", mo.Left, mo.Right);
                    Chunk.EmitOp(OpCode.Mod, _line);
                    break;

                // ── Unary ──
                case NegateOperator n:
                {
                    CompileExpression(n.Value);
                    var negateError = NegateError(TypeOf(n.Value));
                    if (negateError != null) throw new CompileError(negateError, _source, _line, _column);
                    var negated = n.Value;
                    DeferTypedCheck(names => NegateError(TypeOf(negated, names)), negated);
                    Chunk.EmitOp(OpCode.Negate, _line);
                    break;
                }

                case NotOperator no:
                    CompileExpression(no.Value);
                    Chunk.EmitOp(OpCode.Not, _line);
                    break;

                // ── Comparison ──
                case EqualsOperator e:
                    CompileExpression(e.Left);
                    CompileExpression(e.Right);
                    Chunk.EmitOp(OpCode.Equal, _line);
                    break;

                case NotEqualsOperator ne:
                    CompileExpression(ne.Left);
                    CompileExpression(ne.Right);
                    Chunk.EmitOp(OpCode.NotEqual, _line);
                    break;

                case LessThanOperator lt:
                    CompileExpression(lt.Left);
                    CompileExpression(lt.Right);
                    CheckOperands("<", lt.Left, lt.Right);
                    Chunk.EmitOp(OpCode.Less, _line);
                    break;

                case LessThanOrEqualToOperator le:
                    CompileExpression(le.Left);
                    CompileExpression(le.Right);
                    CheckOperands("<=", le.Left, le.Right);
                    Chunk.EmitOp(OpCode.LessEqual, _line);
                    break;

                case GreaterThanOperator gt:
                    CompileExpression(gt.Left);
                    CompileExpression(gt.Right);
                    CheckOperands(">", gt.Left, gt.Right);
                    Chunk.EmitOp(OpCode.Greater, _line);
                    break;

                case GreaterThanOrEqualToOperator ge:
                    CompileExpression(ge.Left);
                    CompileExpression(ge.Right);
                    CheckOperands(">=", ge.Left, ge.Right);
                    Chunk.EmitOp(OpCode.GreaterEqual, _line);
                    break;

                // ── Logical (short-circuit) ──
                case LogicalAndOperator la:
                {
                    CompileExpression(la.Left);
                    var endJump = Chunk.EmitJump(OpCode.JumpIfFalse, _line);
                    Chunk.EmitOp(OpCode.Pop, _line);
                    CompileExpression(la.Right);
                    Chunk.PatchJump(endJump);
                    break;
                }

                case LogicalOrOperator lo:
                {
                    CompileExpression(lo.Left);
                    var endJump = Chunk.EmitJump(OpCode.JumpIfTrue, _line);
                    Chunk.EmitOp(OpCode.Pop, _line);
                    CompileExpression(lo.Right);
                    Chunk.PatchJump(endJump);
                    break;
                }

                // ── Function call ──
                case FunctionExpression f:
                    CompileFunctionCall(f);
                    break;

                // ── Arrays ──
                case ArrayExpression arr:
                    // Checked first: the elements alone would already make
                    // most such functions too large.
                    if (arr.Values.Count > 0xFFFF)
                        throw Chunk.LimitError("array literal too large; build it with <<");
                    foreach (var elem in arr.Values)
                        CompileExpression(elem);
                    Chunk.EmitOp(OpCode.NewArray, _line);
                    Chunk.EmitU16(arr.Values.Count, _line);
                    break;

                case ArrayValueOperator av:
                    CompileExpression(av.Left);
                    CompileExpression(av.Right);
                    Chunk.EmitOp(OpCode.IndexGet, _line);
                    break;

                case ArrayAppendOperator ap:
                    CompileExpression(ap.Left);
                    CompileExpression(ap.Right);
                    CheckOperands("<<", ap.Left, ap.Right);
                    Chunk.EmitOp(OpCode.ArrayAppend, _line);
                    break;

                // ── Class operations ──
                case ClassInstanceOperator ci:
                    // 'new' keyword — operand is a ClassExpression
                    CompileExpression(ci.Value);
                    break;

                case ClassExpression ce:
                    CompileClassInstantiation(ce);
                    break;

                case ClassPropertyOperator cp:
                    CompileClassProperty(cp);
                    break;

                case ClassCastOperator cast:
                {
                    CompileExpression(cast.Left);
                    var typeName = TypeNameOperand(cast.Right, "as");
                    var nameIdx = Chunk.AddConstant(WarValue.FromText(typeName));
                    Chunk.EmitOp(OpCode.CastAs, _line);
                    Chunk.EmitU16(nameIdx, _line);
                    break;
                }

                case ClassInstanceOfOperator iof:
                {
                    CompileExpression(iof.Left);
                    var typeName = TypeNameOperand(iof.Right, "is");
                    var nameIdx = Chunk.AddConstant(WarValue.FromText(typeName));
                    Chunk.EmitOp(OpCode.InstanceOf, _line);
                    Chunk.EmitU16(nameIdx, _line);
                    break;
                }

                case NestedClassInstanceOperator nci:
                    // obj :: new NestedClass[args]
                    CompileExpression(nci.Left); // parent instance
                    if (nci.Right is ClassExpression nestedCe)
                    {
                        CheckInstanceOperand(nci.Left, "Cannot instantiate nested class on non-class value");
                        CheckInstantiation(nestedCe, nested: true);
                        foreach (var arg in nestedCe.PropertiesExpressions)
                            CompileExpression(arg);
                        var nameIdx = Chunk.AddConstant(WarValue.FromText(nestedCe.ClassName));
                        Chunk.EmitOp(OpCode.NewNestedInstance, _line);
                        Chunk.EmitU16(nameIdx, _line);
                        EmitArgCount(nestedCe.PropertiesExpressions.Count, _line);
                    }
                    break;

                // ── ValueReference (used in class constructors) ──
                case Context.ValueReference vr:
                    CompileExpression(new ConstantExpression(vr.Value));
                    break;
            }
        }

        // ── Statically typed operands (T3.8, PROP-28) ──

        /// <summary>
        /// What the compiler knows about an expression's value (PROP-28):
        /// a literal, an interpolated text, an array literal, <c>null</c>,
        /// <c>true</c>/<c>false</c>, or the result of an operator whose type
        /// follows from plan 5.3. No inference through variables or calls.
        /// Indexing is not checked: the grammar only indexes a name
        /// (<c>x{i}</c>, <c>obj :: x{i}</c>), whose type is never known.
        /// A <c>const</c> and an enum member of the module set are typed
        /// once the module set is compiled (<see cref="DeferTypedCheck"/>).
        /// </summary>
        internal enum StaticType { Unknown, Null, Number, Logical, Text, Array }

        /// <summary>The type name an error message uses (<see cref="WarVM.TypeName"/>).</summary>
        private static string StaticTypeName(StaticType type) => type switch
        {
            StaticType.Null => "null",
            StaticType.Number => "number",
            StaticType.Logical => "logical",
            StaticType.Text => "text",
            _ => "array",
        };

        /// <summary>
        /// The type <paramref name="expr"/> evaluates to whenever it
        /// evaluates without an error (PROP-28), else
        /// <see cref="StaticType.Unknown"/>. <paramref name="names"/> types
        /// a bare name or a <c>::</c> (a constant or an enum member, once
        /// the module set is compiled); null: unknown.
        /// </summary>
        private static StaticType TypeOf(IExpression expr, System.Func<IExpression, StaticType>? names = null)
        {
            switch (expr)
            {
                case VariableExpression _:
                case ClassPropertyOperator _:
                    return names?.Invoke(expr) ?? StaticType.Unknown;
                case ConstantExpression c:
                {
                    var v = c.Value;
                    if (v.IsNull) return StaticType.Null;
                    if (v.IsNumeric) return StaticType.Number;
                    if (v.IsLogical) return StaticType.Logical;
                    if (v.IsText) return StaticType.Text;
                    if (v.IsArray) return StaticType.Array;
                    return StaticType.Unknown;
                }
                case ArrayExpression _:
                    return StaticType.Array;
                case AdditionOperator a:
                {
                    var left = TypeOf(a.Left, names);
                    var right = TypeOf(a.Right, names);
                    if (left == StaticType.Text || right == StaticType.Text) return StaticType.Text;
                    if (left == right && (left == StaticType.Number || left == StaticType.Array)) return left;
                    return StaticType.Unknown;
                }
                case MultiplicationOperator m:
                {
                    var left = TypeOf(m.Left, names);
                    var right = TypeOf(m.Right, names);
                    if (left == StaticType.Text || right == StaticType.Text) return StaticType.Text;
                    if (left == StaticType.Number && right == StaticType.Number) return StaticType.Number;
                    return StaticType.Unknown;
                }
                case SubtractionOperator _:
                case DivisionOperator _:
                case ModuloOperator _:
                case NegateOperator _:
                    return StaticType.Number;
                case NotOperator _:
                case EqualsOperator _:
                case NotEqualsOperator _:
                case LessThanOperator _:
                case LessThanOrEqualToOperator _:
                case GreaterThanOperator _:
                case GreaterThanOrEqualToOperator _:
                case ClassInstanceOfOperator _:
                    return StaticType.Logical;
                // 'and'/'or' leave one of their operands.
                case LogicalAndOperator la:
                {
                    var left = TypeOf(la.Left, names);
                    return left == TypeOf(la.Right, names) ? left : StaticType.Unknown;
                }
                case LogicalOrOperator lo:
                {
                    var left = TypeOf(lo.Left, names);
                    return left == TypeOf(lo.Right, names) ? left : StaticType.Unknown;
                }
                // '<<' leaves the array.
                case ArrayAppendOperator ap:
                    return TypeOf(ap.Left, names) == StaticType.Array ? StaticType.Array : StaticType.Unknown;
                // An assignment's value is the value assigned ('x = 5 << 3'
                // is '(x = 5) << 3').
                case AssignmentOperator a:
                    return TypeOf(a.Right, names);
                default:
                    return StaticType.Unknown;
            }
        }

        /// <summary>
        /// The compile error of a binary operator whose operands' types are
        /// both known and rejected by plan 5.3 (PROP-28), with the VM's
        /// message (<c>WarVM.OperandError</c>), at the operator. An operand
        /// that reads a constant or an enum member is checked again once the
        /// module set is compiled (<see cref="DeferTypedCheck"/>).
        /// </summary>
        private void CheckOperands(string op, IExpression leftExpr, IExpression rightExpr)
        {
            var error = OperandError(op, TypeOf(leftExpr), TypeOf(rightExpr));
            if (error != null) throw new CompileError(error, _source, _line, _column);
            DeferTypedCheck(names => OperandError(op, TypeOf(leftExpr, names), TypeOf(rightExpr, names)), leftExpr, rightExpr);
        }

        /// <summary>The VM's message for <paramref name="op"/> on those operand types, or null when they are accepted or not both known.</summary>
        private static string? OperandError(string op, StaticType left, StaticType right)
        {
            if (left == StaticType.Unknown || right == StaticType.Unknown) return null;
            bool ok;
            switch (op)
            {
                case "+":
                    ok = left == StaticType.Text || right == StaticType.Text
                        || left == right && (left == StaticType.Number || left == StaticType.Array);
                    break;
                case "*":
                    ok = left == StaticType.Number && (right == StaticType.Number || right == StaticType.Text)
                        || left == StaticType.Text && right == StaticType.Number;
                    break;
                case "<": case "<=": case ">": case ">=":
                    ok = left == right && (left == StaticType.Number || left == StaticType.Text);
                    break;
                case "<<":
                    ok = left == StaticType.Array;
                    break;
                default: // - / %
                    ok = left == StaticType.Number && right == StaticType.Number;
                    break;
            }
            return ok ? null : "Cannot apply '" + op + "' to " + StaticTypeName(left) + " and " + StaticTypeName(right);
        }

        /// <summary>The VM's message for unary <c>-</c> on an operand of that type, or null.</summary>
        private static string? NegateError(StaticType operand) =>
            operand != StaticType.Unknown && operand != StaticType.Number ? "Cannot apply '-' to " + StaticTypeName(operand) : null;

        /// <summary>
        /// The compile error of a <c>::</c> whose left operand has a known
        /// type (PROP-28): none of them is a class instance.
        /// <paramref name="message"/> is the VM's.
        /// </summary>
        private void CheckInstanceOperand(IExpression left, string message)
        {
            if (TypeOf(left) != StaticType.Unknown)
                throw new CompileError(message, _source, _line, _column);
            DeferTypedCheck(names => TypeOf(left, names) != StaticType.Unknown ? message : null, left);
        }

        /// <summary>
        /// Records <paramref name="check"/> for
        /// <see cref="WarScriptLanguage.CheckTypedOperands"/> when one of
        /// <paramref name="operands"/> has a type that depends on a bare
        /// global read or an <c>E :: x</c> of one (a <c>const</c> or an enum
        /// member: the declaration may come later or in another file), at
        /// the operator's position (PROP-28). The check gets the type of
        /// such a read; it is run once the module set is compiled.
        /// </summary>
        private void DeferTypedCheck(System.Func<System.Func<IExpression, StaticType>, string?> check, params IExpression[] operands)
        {
            var keys = TypedNames(operands);
            if (keys == null) return;
            _script.CheckTypedOperand(name => check(e => TypeOfKey(keys, e, name)), _source, _line, _column);
        }

        /// <summary>
        /// The leaves of <paramref name="operands"/> that <see cref="TypeOf"/>
        /// would ask about and that read a constant or an enum member if
        /// anything (<see cref="TypedNameKey"/>), with their keys; null when
        /// there are none or the module set is not being compiled.
        /// </summary>
        private List<(IExpression Expression, string Key)>? TypedNames(params IExpression[] operands)
        {
            if (!_script.CollectsTypedOperands) return null;
            List<(IExpression, string)>? keys = null;
            foreach (var operand in operands)
                TypeOf(operand, e =>
                {
                    var key = TypedNameKey(e);
                    if (key != null) (keys ??= new List<(IExpression, string)>()).Add((e, key));
                    return StaticType.Unknown;
                });
            return keys;
        }

        private static StaticType TypeOfKey(List<(IExpression Expression, string Key)> keys, IExpression e, System.Func<string, StaticType> name)
        {
            foreach (var (expression, key) in keys)
                if (ReferenceEquals(expression, e)) return name(key);
            return StaticType.Unknown;
        }

        /// <summary>
        /// <c>X</c> for a bare global read of <c>X</c>, <c>E::x</c> for
        /// <c>E :: x</c> and <c>E::m[]</c> for <c>E :: m [a]</c> where
        /// <c>E</c> is a bare global read; else null. Not in a class body,
        /// where a bare read may find a field of the instance first (of any
        /// class part, Q-16).
        /// </summary>
        private string? TypedNameKey(IExpression e)
        {
            if (_classBody) return null;
            switch (e)
            {
                case VariableExpression v:
                    return ResolveLocal(v.Name) == -1 ? v.Name : null;
                case ClassPropertyOperator cp when cp.Left is VariableExpression enumName && ResolveLocal(enumName.Name) == -1:
                    if (cp.Right is VariableExpression member) return enumName.Name + "::" + member.Name;
                    // Only a call with the one argument `name` takes (another
                    // count is an error of its own at runtime).
                    if (cp.Right is FunctionExpression method && method.ArgumentExpression.Count == 1)
                        return enumName.Name + "::" + method.Name + "[]";
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The type of a <c>const</c>'s value for
        /// <see cref="WarScriptLanguage.CheckTypedOperands"/>, given the
        /// types of the constants and enum members it reads.
        /// </summary>
        private System.Func<System.Func<string, StaticType>, StaticType> ConstantType(IExpression value)
        {
            var keys = TypedNames(value);
            if (keys == null)
            {
                var type = TypeOf(value);
                return _ => type;
            }
            return name => TypeOf(value, e => TypeOfKey(keys, e, name));
        }

        // ── Function call ──
        private void CompileFunctionCall(FunctionExpression call)
        {
            // If the call target resolves as a local variable (e.g. a lambda parameter),
            // use CallValue which pops the function value from the stack.
            // Otherwise use the name-based Call opcode (handles definitions + imports +
            // global variables holding lambdas via the MemoryScope fallback).
            var localSlot = ResolveLocal(call.Name);
            if (localSlot != -1)
            {
                CheckRead(call.Name, localSlot, call.Line, call.Column);
                // Push the function value, then arguments
                Chunk.EmitOp(OpCode.GetLocal, _line);
                Chunk.EmitU16(localSlot, _line);
                foreach (var arg in call.ArgumentExpression)
                    CompileExpression(arg);
                Chunk.EmitOp(OpCode.CallValue, _line);
                EmitArgCount(call.ArgumentExpression.Count, _line);
            }
            else
            {
                foreach (var arg in call.ArgumentExpression)
                    CompileExpression(arg);
                CheckCall(call);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(call.Name));
                Chunk.EmitOp(OpCode.Call, _line);
                Chunk.EmitU16(nameIdx, _line);
                EmitArgCount(call.ArgumentExpression.Count, _line);
            }
        }

        /// <summary>
        /// A call by name (<c>Call</c>, <c>TailCall</c>) must name a function
        /// taking that many arguments, or a global that may hold a function
        /// value (T3.8): recorded for <see cref="WarScriptLanguage.CheckCall"/>,
        /// checked once the module set is compiled, at the function name. In
        /// a class body the VM's fallback looks in the instance's fields
        /// first, so a call naming a field of the class (or any call, when
        /// its fields are unknown) is not recorded.
        /// </summary>
        private void CheckCall(FunctionExpression call)
        {
            if (_classBody && (_classFields == null || _classFields.Contains(call.Name))) return;
            var onThisLine = call.Line > 0 && call.Line == _line;
            _script.CheckCall(call.Name, call.ArgumentExpression.Count, _classBody, _source,
                onThisLine ? call.Line : _line, onThisLine ? call.Column : _column);
        }

        // ── Class instantiation: new ClassName[args] ──
        private void CompileClassInstantiation(ClassExpression ce)
        {
            CheckInstantiation(ce, nested: false);
            foreach (var arg in ce.PropertiesExpressions)
                CompileExpression(arg);

            var nameIdx = Chunk.AddConstant(WarValue.FromText(ce.ClassName));
            Chunk.EmitOp(OpCode.NewInstance, _line);
            Chunk.EmitU16(nameIdx, _line);
            EmitArgCount(ce.PropertiesExpressions.Count, _line);
        }

        /// <summary>
        /// <c>new C [...]</c> must name a class taking that many arguments
        /// (T3.8, PROP-29): checked once the module set is compiled, at the
        /// class name.
        /// </summary>
        private void CheckInstantiation(ClassExpression ce, bool nested)
        {
            var onThisLine = ce.Line == _line && ce.Column > 0;
            _script.CheckInstantiation(ce.ClassName, ce.PropertiesExpressions.Count, nested, _source,
                _line, onThisLine ? ce.Column : _column);
        }

        /// <summary>
        /// The class name on the right of <c>as</c> / <c>is</c>: anything
        /// but a plain name is a <see cref="CompileError"/> (CRASH-6), not
        /// an <c>InvalidCastException</c>; a name that is no class of the
        /// module set or the host is one too (T3.8), reported by
        /// <see cref="WarScriptLanguage.CheckInstantiation"/>.
        /// </summary>
        private string TypeNameOperand(IExpression right, string keyword)
        {
            if (right is VariableExpression type)
            {
                // It must name a class (T3.8): checked with the `new`s once
                // the module set is compiled, at any number of arguments.
                var onThisLine = type.Line == _line && type.Column > 0;
                _script.CheckInstantiation(type.Name, -1, false, _source,
                    _line, onThisLine ? type.Column : _column);
                return type.Name;
            }
            throw new CompileError("'" + keyword + "' must be followed by a class name", _source, _line, _column);
        }

        // ── Class property access: obj::prop, obj::method[args], obj::arr{i} ──
        private void CompileClassProperty(ClassPropertyOperator cp)
        {
            // ── Superinstruction: this :: prop → ThisGetProperty ──
            if (cp.Left is ThisExpression && cp.Right is VariableExpression thisVar)
            {
                CheckField(thisVar.Name, thisVar.Line, thisVar.Column);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(thisVar.Name));
                Chunk.EmitOp(OpCode.ThisGetProperty, _line);
                Chunk.EmitU16(nameIdx, _line);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), _line);
                return;
            }
            // ── Superinstruction: this :: arr{i} → ThisGetProperty + IndexGet ──
            if (cp.Left is ThisExpression && cp.Right is ArrayValueOperator thisArrOp
                && thisArrOp.Left is VariableExpression thisArrVar)
            {
                CheckField(thisArrVar.Name, thisArrVar.Line, thisArrVar.Column);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(thisArrVar.Name));
                Chunk.EmitOp(OpCode.ThisGetProperty, _line);
                Chunk.EmitU16(nameIdx, _line);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), _line);
                CompileExpression(thisArrOp.Right);
                Chunk.EmitOp(OpCode.IndexGet, _line);
                return;
            }

            CompileExpression(cp.Left); // push instance

            if (cp.Right is VariableExpression varExpr)
            {
                CheckInstanceOperand(cp.Left, "Cannot access property '" + varExpr.Name + "' on non-class value");
                CheckProperty(cp.Left, varExpr.Name, varExpr.Line, varExpr.Column);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(varExpr.Name));
                Chunk.EmitOp(OpCode.GetProperty, _line);
                Chunk.EmitU16(nameIdx, _line);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), _line);
            }
            else if (cp.Right is FunctionExpression funcExpr)
            {
                // Method call: obj::method[args]
                CheckInstanceOperand(cp.Left, "Cannot call method '" + funcExpr.Name + "' on non-class value");
                CheckProperty(cp.Left, funcExpr.Name, funcExpr.Line, funcExpr.Column);
                foreach (var arg in funcExpr.ArgumentExpression)
                    CompileExpression(arg);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(funcExpr.Name));
                Chunk.EmitOp(OpCode.CallMethod, _line);
                Chunk.EmitU16(nameIdx, _line);
                EmitArgCount(funcExpr.ArgumentExpression.Count, _line);
            }
            else if (cp.Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
            {
                // obj::arr{i}  →  GetProperty then IndexGet
                CheckInstanceOperand(cp.Left, "Cannot access property '" + arrayVar.Name + "' on non-class value");
                CheckProperty(cp.Left, arrayVar.Name, arrayVar.Line, arrayVar.Column);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(arrayVar.Name));
                Chunk.EmitOp(OpCode.GetProperty, _line);
                Chunk.EmitU16(nameIdx, _line);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), _line);
                CompileExpression(arrayOp.Right);
                Chunk.EmitOp(OpCode.IndexGet, _line);
            }
        }
    
        /// <summary>
        /// The resolution pre-pass of a function, method or lambda body
        /// (T3.4): collects the names it assigns (its locals, PROP-9) and
        /// the names it declares <c>global</c>, and checks the loop and
        /// rescue variables: one may not shadow a parameter, a local, a
        /// declared global or an enclosing loop or rescue variable.
        /// Lambdas and nested definitions are bodies of their own.
        /// For a class body (<c>classBody</c>) the assigned names are its
        /// fields instead (<see cref="Fields"/>: also the <c>@x = ...</c>
        /// targets), and nothing is checked.
        /// </summary>
        private sealed class BodyNames
        {
            /// <summary>The assigned names, in order of first assignment (a class body's are fields).</summary>
            public readonly List<string> Locals = new();
            private readonly HashSet<string> _localSet = new();
            private readonly HashSet<string> _globals = new();
            private readonly HashSet<string> _thisTargets = new();

            /// <summary>The names declared <c>global</c> anywhere in the body.</summary>
            public HashSet<string> Globals => _globals;

            /// <summary>For a class body: the fields it declares (assigned names and <c>@x = ...</c> targets).</summary>
            public IEnumerable<string> Fields
            {
                get
                {
                    foreach (var name in Locals) yield return name;
                    foreach (var name in _thisTargets) yield return name;
                }
            }
            private readonly HashSet<string> _parameters;
            private readonly List<string> _blockVariables = new();
            private readonly List<(string Name, string What, int Line, int Column, bool ShadowsBlock)> _declarations = new();

            private BodyNames(IReadOnlyList<string> parameters)
            {
                _parameters = new HashSet<string>(parameters);
            }

            public static BodyNames Collect(IReadOnlyList<string> parameters, CompositeStatement body, string? source,
                bool classBody = false)
            {
                var names = new BodyNames(parameters);
                names.WalkBlock(body);
                if (classBody) return names;
                foreach (var d in names._declarations)
                {
                    string? reason = null;
                    if (d.ShadowsBlock) reason = "an enclosing loop or rescue variable";
                    else if (names._parameters.Contains(d.Name)) reason = "a parameter";
                    else if (names._localSet.Contains(d.Name)) reason = "a local variable";
                    else if (names._globals.Contains(d.Name)) reason = "a name declared global";
                    if (reason != null)
                        throw new CompileError(d.What + " '" + d.Name + "' shadows " + reason + " of the same body", source, d.Line, d.Column);
                }
                return names;
            }

            /// <summary>
            /// The names <paramref name="statement"/> assigns bare (not its
            /// loop or rescue variables), for <see cref="Recover"/>.
            /// </summary>
            public static List<string> Assigned(Statement.Statement statement)
            {
                var names = new BodyNames(System.Array.Empty<string>());
                names.Walk(statement);
                return names.Locals;
            }

            private void WalkBlock(CompositeStatement block)
            {
                foreach (var statement in block.StatementsToExecute)
                    Walk(statement);
            }

            private void Walk(Statement.Statement statement)
            {
                switch (statement)
                {
                    case GlobalStatement g:
                        foreach (var name in g.Names) _globals.Add(name);
                        break;
                    case ExpressionStatement e: Walk(e.Expression); break;
                    case PrintStatement p:      Walk(p.Expression); break;
                    case ReturnStatement r:     Walk(r.Expression); break;
                    case AssertStatement a:     Walk(a.Expression); break;
                    case RaiseExceptionStatement r: Walk(r.Expression); break;
                    case YieldStatement y:      Walk(y.Expression); break;
                    case ConditionStatement c:
                        foreach (var branch in c.Cases)
                        {
                            Walk(branch.Key);
                            WalkBlock(branch.Value);
                        }
                        break;
                    case WhileLoopStatement w:
                        Walk(w.Condition);
                        WalkBlock(w);
                        break;
                    case ForLoopStatement f:
                        Walk(f.LowerBound);
                        Walk(f.UpperBound);
                        Walk(f.Step);
                        WalkBlockWith(f.Variable.Name, "loop variable", f, f);
                        break;
                    case IterableLoopStatement l:
                        Walk(l.Iterable);
                        WalkBlockWith(l.Variable.Name, "loop variable", l, l);
                        break;
                    case HandleExceptionStatement h:
                        WalkBlock(h.BeginStatement);
                        if (h.RescueStatement != null)
                        {
                            if (h.ErrorVariable != null)
                                WalkBlockWith(h.ErrorVariable, "rescue variable", h, h.RescueStatement);
                            else
                                WalkBlock(h.RescueStatement);
                        }
                        if (h.EnsureStatement != null)
                            WalkBlock(h.EnsureStatement);
                        break;
                    // Function and class definitions are bodies of their
                    // own; imports cannot appear here (T3.3).
                }
            }

            private void WalkBlockWith(string variable, string what, Statement.Statement at, CompositeStatement block)
            {
                _declarations.Add((variable, what, at.RowNumber ?? 0, at.Column, _blockVariables.Contains(variable)));
                _blockVariables.Add(variable);
                WalkBlock(block);
                _blockVariables.RemoveAt(_blockVariables.Count - 1);
            }

            private void Walk(IExpression? expression)
            {
                switch (expression)
                {
                    case null:
                    case LambdaExpression _:
                        break;
                    case AssignmentOperator a:
                        Walk(a.Right);
                        if (a.Left is VariableExpression v) Assign(v.Name);
                        else
                        {
                            if (a.Left is ClassPropertyOperator { Left: ThisExpression, Right: VariableExpression field })
                                _thisTargets.Add(field.Name);
                            Walk(a.Left);
                        }
                        break;
                    case BinaryOperatorExpression b:
                        Walk(b.Left);
                        Walk(b.Right);
                        break;
                    case UnaryOperatorExpression u:
                        Walk(u.Value);
                        break;
                    case FunctionExpression f:
                        foreach (var argument in f.ArgumentExpression) Walk(argument);
                        break;
                    case ArrayExpression array:
                        foreach (var value in array.Values) Walk(value);
                        break;
                    case ClassExpression c:
                        foreach (var argument in c.PropertiesExpressions) Walk(argument);
                        break;
                }
            }

            private void Assign(string name)
            {
                // A write to a loop or rescue variable inside its block, to a
                // parameter, or to a name declared global is not a new local.
                if (_blockVariables.Contains(name) || _parameters.Contains(name) || _globals.Contains(name))
                    return;
                if (_localSet.Add(name))
                    Locals.Add(name);
            }
        }
}
}
