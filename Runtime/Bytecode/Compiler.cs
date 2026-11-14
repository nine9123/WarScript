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
            public int RuntimeScopeDepth;
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
            public int RuntimeScopeDepth;  // outside the block
            public int LocalCount;         // outside the block
            public TryRegion Region;
            public CompositeStatement? Ensure;
            public readonly List<int> DropPendingPatches = new();
        }

        private readonly List<TryCtx> _tries = new();
        private readonly List<string> _localNames = new();

        /// <summary>
        /// Tracks emitted PushScope/PopScope pairs at compile time.
        /// Break/next use this to emit the right number of PopScope ops.
        /// </summary>
        private int _runtimeScopeDepth;

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
        /// top-level code and class bodies, which keep v1's scopes for now.
        /// </summary>
        private bool _resolveNames;

        /// <summary>The body's locals assigned so far, in source order (read before assignment, PROP-9).</summary>
        private readonly HashSet<string> _assignedSoFar = new();

        /// <summary>Every bare name the body has read or written so far (<c>global</c> must come first, PROP-10).</summary>
        private readonly HashSet<string> _usedNames = new();

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
                CompileFunctionDef(funcDef);
            }
            // Compile all classes (methods + constructor bodies)
            foreach (var classDef in scope.ClassDefinitions)
                CompileClassDef(classDef);
        }

        private void CompileFunctionDef(FunctionDefinition funcDef)
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

            child.BeginBody(funcDef.Details.Arguments, funcDef.Statement);

            // Compile function body (nested definitions are compiled separately
            // via the flat CompileAllDefinitions walk)
            foreach (var stmt in funcDef.Statement.StatementsToExecute)
                child.CompileStatement(stmt);

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

            // Compile all methods
            foreach (var funcDef in classScope.Functions)
            {
                if (funcDef is NativeFunctionDefinition) continue;
                CompileFunctionDef(funcDef);
            }

            // Compile nested classes
            foreach (var nestedClass in classScope.ClassDefinitions)
                CompileClassDef(nestedClass);

            // Compile constructor body (non-definition statements only)
            CompileConstructorBody(classDef);
        }

        private void CompileConstructorBody(ClassDefinition classDef)
        {
            var stmts = classDef.Statement.StatementsToExecute;
            if (stmts.Count == 0) return;

            // Constructor runs at "depth 0" so property accesses go through
            // GetGlobal/SetGlobal which hit the class instance's MemoryScope
            var child = new Compiler(_script, functionDepth: 0);
            child._source = _source;
            child._current = new CompiledFunction(classDef.ClassDetails.Name + "#ctor", 0);
            child._current.Chunk.Source = _source;
            child._line = Line(classDef.Statement);
            child.SetColumn(classDef.Statement.Column);

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
        private static void VerifyOutput(CompiledFunction function)
        {
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

            foreach (var stmt in lambda.Body.StatementsToExecute)
                compiler.CompileStatement(stmt);

            compiler.EmitReturn(0);
            compiler._current.LocalCount = compiler._maxLocals;
            compiler._current.LocalNames = compiler._localNames.ToArray();
            compiler._current.Chunk.FinalizePropertyCaches();
            VerifyOutput(compiler._current);
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
        /// body, initialized to null here, in the order of first assignment
        /// (PROP-9). Loop and rescue variables get theirs when their block
        /// starts. Reports the scoping errors <see cref="BodyNames"/> finds.
        /// </summary>
        private void BeginBody(IReadOnlyList<string> parameters, CompositeStatement body)
        {
            _resolveNames = true;
            BeginScope();
            foreach (var parameter in parameters)
                AddLocal(parameter, LocalKind.Parameter);

            var names = BodyNames.Collect(parameters, body, _source);
            foreach (var name in names.Locals)
            {
                Chunk.EmitOp(OpCode.Null, _line);
                AddLocal(name, LocalKind.BodyLocal);
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

        // A function, method or lambda body keeps every variable in a stack
        // slot (T3.4), so its blocks need no runtime scope: the only
        // MemoryScope it runs in is its call's, which SetGlobal skips.
        private void EmitPushScope(int line)
        {
            if (_resolveNames) return;
            Chunk.EmitOp(OpCode.PushScope, line);
            _runtimeScopeDepth++;
        }

        private void EmitPopScope(int line)
        {
            if (_resolveNames) return;
            Chunk.EmitOp(OpCode.PopScope, line);
            _runtimeScopeDepth--;
        }

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
            try { CompileStatementBody(stmt); }
            catch (CompileError e) when (e.Line == 0 && _line > 0)
            {
                // A limit found while emitting (T1.8) carries no line: report
                // the innermost statement that has one.
                throw new CompileError(e.Reason, e.ScriptName, _line, _column);
            }
            catch (SyntaxException e) when (!(e is CompileError))
            {
                // Likewise for the compiler's other errors (T3.1).
                e.Locate(_line, _column).LocateIn(_source, null);
                throw;
            }
            finally
            {
                _line = outerLine;
                SetColumn(outerColumn);
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
                case ImportStatement s:       CompileImport(s);             break;
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
                var slot = ResolveLocal(v.Name);
                if (slot != -1)
                {
                    Chunk.EmitOp(OpCode.SetLocal, line);
                    Chunk.EmitU16(slot, line);
                    if (_locals[slot].Kind == LocalKind.BodyLocal)
                        _assignedSoFar.Add(v.Name);
                    return false;
                }
                // Not a stack slot. In a function, method or lambda body
                // that is a name declared `global` (T3.4): the VM writes the
                // instance globals, creating the name if needed. At the top
                // level and in a class body, SetGlobal still goes through the
                // MemoryScope chain ("walk up, create if missing").
                var nameIdx = Chunk.AddConstant(WarValue.FromText(v.Name));
                Chunk.EmitOp(OpCode.SetGlobal, line);
                Chunk.EmitU16(nameIdx, line);
                return false;
            }

            // ── Array index assignment: arr{i} = expr ──
            if (assign.Left is ArrayValueOperator av)
            {
                // If the target is a simple variable, use IndexSetLocal/Global
                // which handles text mutation writeback
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
                        CompileExpression(assign.Right);  // value
                        var nameIdx = Chunk.AddConstant(WarValue.FromText(propName.Name));
                        Chunk.EmitOp(OpCode.ThisSetProperty, line);
                        Chunk.EmitU16(nameIdx, line);
                        Chunk.EmitU16(Chunk.AllocCacheSlot(), line);
                        return false;
                    }

                    CompileExpression(cp.Left);      // instance
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
                    CompileExpression(cp.Left);         // instance
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

                // Compile body in its own scope
                // Only emit PushScope/PopScope at top-level script body.
                // Inside functions, SetGlobal→MemoryScope handles scoping.
                bool emitScope = _functionDepth == 0;
                if (emitScope) EmitPushScope(line);
                BeginScope();
                foreach (var s in body.StatementsToExecute)
                    CompileStatement(s);
                EndScope(line);
                if (emitScope) EmitPopScope(line);

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
                LocalCountAtLoop = _locals.Count,
                RuntimeScopeDepth = _runtimeScopeDepth
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

            var loopStart = Chunk.Count;

            _loops.Add(new LoopCtx
            {
                LoopStart = loopStart,
                ContinueTarget = 0,
                BreakJumps = new List<int>(),
                NextJumps = new List<int>(),
                UsesForwardNext = true,
                ScopeDepth = _scopeDepth,
                LocalCountAtLoop = _locals.Count,
                RuntimeScopeDepth = _runtimeScopeDepth
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
                LocalCountAtLoop = _locals.Count,
                RuntimeScopeDepth = _runtimeScopeDepth
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
            public int RuntimeScopeDepth;
            public int ScopeDepth;
            public TryCtx[] Tries;
        }

        // 'break' and 'next' emit the code that leaves the blocks they are
        // in, but the statements after them (unreachable) are compiled as if
        // still inside: the compile-time state is put back afterwards.
        private BlockState SaveBlockState() => new BlockState
        {
            Locals = _locals.ToArray(),
            RuntimeScopeDepth = _runtimeScopeDepth,
            ScopeDepth = _scopeDepth,
            Tries = _tries.ToArray()
        };

        private void RestoreBlockState(in BlockState state)
        {
            _locals.Clear();
            _locals.AddRange(state.Locals);
            _runtimeScopeDepth = state.RuntimeScopeDepth;
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
                EmitUnwindTo(t.RuntimeScopeDepth, t.LocalCount, line);
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
            EmitUnwindTo(loop.RuntimeScopeDepth, loop.LocalCountAtLoop, line);
        }

        /// <summary>Pops runtime scopes and stack locals down to the given counts.</summary>
        private void EmitUnwindTo(int runtimeScopeDepth, int localCount, int line)
        {
            while (_runtimeScopeDepth > runtimeScopeDepth)
                EmitPopScope(line);
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
                RuntimeScopeDepth = _runtimeScopeDepth,
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
            EmitPushScope(line);
            BeginScope();
            foreach (var s in stmt.BeginStatement.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);
            EmitPopScope(line);

            // Normal exit: pop handler, fall through to ensure
            tryCtx.Region = TryRegion.Rescue;
            Chunk.EmitOp(OpCode.PopHandler, line);
            var jumpToEnsure = Chunk.EmitJump(OpCode.Jump, line);

            // ── Rescue body ──
            int rescueIP = Chunk.Count;
            if (stmt.RescueStatement != null)
            {
                EmitPushScope(line);
                BeginScope();
                // The VM always pushes the error value before jumping here.
                if (stmt.ErrorVariable != null)
                    AddLocal(stmt.ErrorVariable);
                else
                    AddLocal("$error");
                foreach (var s in stmt.RescueStatement.StatementsToExecute)
                    CompileStatement(s);
                EndScope(line);
                EmitPopScope(line);
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
            EmitPushScope(line);
            BeginScope();
            foreach (var s in ensure.StatementsToExecute)
                CompileStatement(s);
            EndScope(line);
            EmitPopScope(line);
        }

        // ── Global declaration (T3.4, PROP-10) ──
        private void CompileGlobal(GlobalStatement stmt)
        {
            // In a class body the declaration takes effect with the class
            // field list (T3.4, second half); until then it changes nothing.
            if (!_resolveNames) return;
            foreach (var name in stmt.Names)
            {
                var slot = ResolveLocal(name);
                if (slot != -1 && _locals[slot].Kind == LocalKind.Parameter)
                    throw new CompileError("'" + name + "' is a parameter and cannot be declared global", _source, _line, _column);
                if (_usedNames.Contains(name))
                    throw new CompileError("'global " + name + "' must come before any use of '" + name + "' in this body", _source, _line, _column);
            }
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
                if (_locals[slot].Kind == LocalKind.BodyLocal && _inlinedEnsure == 0 && !_assignedSoFar.Contains(name))
                    throw new CompileError("variable '" + name + "' is read before it is assigned", _source, line, column);
                return;
            }
            if (_enclosingLocals != null && _enclosingLocals.Contains(name))
                throw new CompileError("closures are not supported: '" + name + "' is a local variable of the code around the lambda", _source, line, column);
        }

        // ── Import ──
        private void CompileImport(ImportStatement stmt)
        {
            var pathIdx = Chunk.AddConstant(WarValue.FromText(stmt.Path));
            Chunk.EmitOp(OpCode.Import, Line(stmt));
            Chunk.EmitU16(pathIdx, Line(stmt));
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
                    var funcValue = WarValue.FromNativeObject(compiled);
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
                    Chunk.EmitOp(OpCode.Add, _line);
                    break;

                case SubtractionOperator s:
                    CompileExpression(s.Left);
                    CompileExpression(s.Right);
                    Chunk.EmitOp(OpCode.Sub, _line);
                    break;

                case MultiplicationOperator m:
                    CompileExpression(m.Left);
                    CompileExpression(m.Right);
                    Chunk.EmitOp(OpCode.Mul, _line);
                    break;

                case DivisionOperator d:
                    CompileExpression(d.Left);
                    CompileExpression(d.Right);
                    Chunk.EmitOp(OpCode.Div, _line);
                    break;

                case ModuloOperator mo:
                    CompileExpression(mo.Left);
                    CompileExpression(mo.Right);
                    Chunk.EmitOp(OpCode.Mod, _line);
                    break;

                // ── Unary ──
                case NegateOperator n:
                    CompileExpression(n.Value);
                    Chunk.EmitOp(OpCode.Negate, _line);
                    break;

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
                    Chunk.EmitOp(OpCode.Less, _line);
                    break;

                case LessThanOrEqualToOperator le:
                    CompileExpression(le.Left);
                    CompileExpression(le.Right);
                    Chunk.EmitOp(OpCode.LessEqual, _line);
                    break;

                case GreaterThanOperator gt:
                    CompileExpression(gt.Left);
                    CompileExpression(gt.Right);
                    Chunk.EmitOp(OpCode.Greater, _line);
                    break;

                case GreaterThanOrEqualToOperator ge:
                    CompileExpression(ge.Left);
                    CompileExpression(ge.Right);
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
                var nameIdx = Chunk.AddConstant(WarValue.FromText(call.Name));
                Chunk.EmitOp(OpCode.Call, _line);
                Chunk.EmitU16(nameIdx, _line);
                EmitArgCount(call.ArgumentExpression.Count, _line);
            }
        }

        // ── Class instantiation: new ClassName[args] ──
        private void CompileClassInstantiation(ClassExpression ce)
        {
            foreach (var arg in ce.PropertiesExpressions)
                CompileExpression(arg);

            var nameIdx = Chunk.AddConstant(WarValue.FromText(ce.ClassName));
            Chunk.EmitOp(OpCode.NewInstance, _line);
            Chunk.EmitU16(nameIdx, _line);
            EmitArgCount(ce.PropertiesExpressions.Count, _line);
        }

        /// <summary>
        /// The class name on the right of <c>as</c> / <c>is</c>: anything
        /// but a plain name is a <see cref="CompileError"/> (CRASH-6), not
        /// an <c>InvalidCastException</c>.
        /// </summary>
        private string TypeNameOperand(IExpression right, string keyword)
        {
            if (right is VariableExpression type) return type.Name;
            throw new CompileError("'" + keyword + "' must be followed by a class name", _source, _line, _column);
        }

        // ── Class property access: obj::prop, obj::method[args], obj::arr{i} ──
        private void CompileClassProperty(ClassPropertyOperator cp)
        {
            // ── Superinstruction: this :: prop → ThisGetProperty ──
            if (cp.Left is ThisExpression && cp.Right is VariableExpression thisVar)
            {
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
                var nameIdx = Chunk.AddConstant(WarValue.FromText(varExpr.Name));
                Chunk.EmitOp(OpCode.GetProperty, _line);
                Chunk.EmitU16(nameIdx, _line);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), _line);
            }
            else if (cp.Right is FunctionExpression funcExpr)
            {
                // Method call: obj::method[args]
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
        /// </summary>
        private sealed class BodyNames
        {
            public readonly List<string> Locals = new();
            private readonly HashSet<string> _localSet = new();
            private readonly HashSet<string> _globals = new();
            private readonly HashSet<string> _parameters;
            private readonly List<string> _blockVariables = new();
            private readonly List<(string Name, string What, int Line, int Column, bool ShadowsBlock)> _declarations = new();

            private BodyNames(IReadOnlyList<string> parameters)
            {
                _parameters = new HashSet<string>(parameters);
            }

            public static BodyNames Collect(IReadOnlyList<string> parameters, CompositeStatement body, string? source)
            {
                var names = new BodyNames(parameters);
                names.WalkBlock(body);
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
                        else Walk(a.Left);
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
