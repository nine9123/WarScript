#nullable enable

using System.Collections.Generic;
using WarScript.Context.Definition;
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
        private Chunk Chunk => _current.Chunk;

        private readonly List<Local> _locals = new();
        private int _scopeDepth;
        private int _maxLocals;
        private readonly List<LoopCtx> _loops = new();
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
            _current = new CompiledFunction("<script>", 0);

            // Compile all function/class definitions reachable from this scope
            CompileDefinitions(defScope);

            // Compile main body
            foreach (var stmt in program.StatementsToExecute)
                CompileStatement(stmt);

            EmitReturn(0);
            _current.LocalCount = _maxLocals;
            _current.LocalNames = _localNames.ToArray();
            Chunk.FinalizePropertyCaches();
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

            var child = new Compiler(_script, functionDepth: 1);
            child._current = new CompiledFunction(funcDef.Details.Name, funcDef.Details.Arguments.Count);

            child.BeginScope();

            // Parameters occupy the first N local slots
            foreach (var arg in funcDef.Details.Arguments)
                child.AddLocal(arg);

            // Compile function body (nested definitions are compiled separately
            // via the flat CompileAllDefinitions walk)
            foreach (var stmt in funcDef.Statement.StatementsToExecute)
                child.CompileStatement(stmt);

            child.EmitReturn(0);
            child._current.LocalCount = child._maxLocals;
            child._current.LocalNames = child._localNames.ToArray();
            child._current.Chunk.FinalizePropertyCaches();
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
            child._current = new CompiledFunction(classDef.ClassDetails.Name + "#ctor", 0);

            foreach (var stmt in stmts)
                child.CompileStatement(stmt);

            child.EmitReturn(0);
            child._current.LocalCount = child._maxLocals;
            child._current.LocalNames = child._localNames.ToArray();
            child._current.Chunk.FinalizePropertyCaches();
            classDef.CompiledConstructor = child._current;
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

        private int AddLocal(string name)
        {
            var slot = _locals.Count;
            _locals.Add(new Local { Name = name, Depth = _scopeDepth });
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
            if (popCount == 1) Chunk.EmitOp(OpCode.Pop, line);
            else if (popCount > 1) { Chunk.EmitOp(OpCode.PopN, line); Chunk.EmitByte((byte)popCount, line); }
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

        private void EmitPushScope(int line)
        {
            Chunk.EmitOp(OpCode.PushScope, line);
            _runtimeScopeDepth++;
        }

        private void EmitPopScope(int line)
        {
            Chunk.EmitOp(OpCode.PopScope, line);
            _runtimeScopeDepth--;
        }

        private static int Line(Statement.Statement stmt) => stmt.RowNumber ?? 0;

        // ────────────────────────────────────────────────────────
        //  Statement compilation
        // ────────────────────────────────────────────────────────

        private void CompileStatement(Statement.Statement stmt)
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
                var slot = ResolveLocal(v.Name);
                if (slot != -1)
                {
                    Chunk.EmitOp(OpCode.SetLocal, line);
                    Chunk.EmitU16(slot, line);
                    return false;
                }
                // Not a known local (parameter or loop var).
                // Use SetGlobal — this goes through MemoryScope which
                // replicates the tree-walker's "walk up, create if missing" semantics.
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
                    var slot = ResolveLocal(avVar.Name);
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

            // Fallback: compile as general expression
            CompileExpression(assign);
            return false;
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
            if (_functionDepth > 0
                && _tryDepth == 0
                && stmt.Expression is FunctionExpression tailCall)
            {
                int line = Line(stmt);
                foreach (var arg in tailCall.ArgumentExpression)
                    CompileExpression(arg);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(tailCall.Name));
                Chunk.EmitOp(OpCode.TailCall, line);
                Chunk.EmitU16(nameIdx, line);
                Chunk.EmitByte((byte)tailCall.ArgumentExpression.Count, line);
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
            if (_loops.Count == 0) return;
            int line = Line(stmt);

            var loop = _loops[_loops.Count - 1];
            // Pop runtime scopes opened since the loop started (e.g. from if-blocks)
            var scopesToPop = _runtimeScopeDepth - loop.RuntimeScopeDepth;
            for (int i = 0; i < scopesToPop; i++)
                EmitPopScope(line);
            // Pop locals down to the loop's entry point
            var popCount = _locals.Count - loop.LocalCountAtLoop;
            if (popCount > 0)
            {
                Chunk.EmitOp(OpCode.PopN, line);
                Chunk.EmitByte((byte)popCount, line);
            }
            loop.BreakJumps.Add(Chunk.EmitJump(OpCode.Jump, line));
        }

        // ── Next (continue) ──
        private void CompileNext(NextStatement stmt)
        {
            if (_loops.Count == 0) return;
            int line = Line(stmt);

            var loop = _loops[_loops.Count - 1];
            var scopesToPop = _runtimeScopeDepth - loop.RuntimeScopeDepth;
            for (int i = 0; i < scopesToPop; i++)
                EmitPopScope(line);
            var popCount = _locals.Count - loop.LocalCountAtLoop;
            if (popCount > 0)
            {
                Chunk.EmitOp(OpCode.PopN, line);
                Chunk.EmitByte((byte)popCount, line);
            }
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
                // No rescue block — pop the error value the VM pushes
                Chunk.EmitOp(OpCode.Pop, line);
            }
            // After rescue, fall through to ensure (no jump needed)

            // ── Ensure body ──
            int ensureIP = Chunk.Count;
            Chunk.PatchJump(jumpToEnsure);
            if (stmt.EnsureStatement != null)
            {
                EmitPushScope(line);
                BeginScope();
                foreach (var s in stmt.EnsureStatement.StatementsToExecute)
                    CompileStatement(s);
                EndScope(line);
                EmitPopScope(line);
            }

            int endIP = Chunk.Count;

            // Patch the PushHandler operands (absolute IPs)
            Chunk.Code[patchBase]     = (byte)((rescueIP >> 8) & 0xFF);
            Chunk.Code[patchBase + 1] = (byte)(rescueIP & 0xFF);
            Chunk.Code[patchBase + 2] = (byte)((ensureIP >> 8) & 0xFF);
            Chunk.Code[patchBase + 3] = (byte)(ensureIP & 0xFF);
            Chunk.Code[patchBase + 4] = (byte)((endIP >> 8) & 0xFF);
            Chunk.Code[patchBase + 5] = (byte)(endIP & 0xFF);

            _tryDepth--;
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
            switch (expr)
            {
                // ── Literals ──
                case ConstantExpression c:
                {
                    var val = c.Value;
                    if (val.IsNull) Chunk.EmitOp(OpCode.Null, 0);
                    else if (val.IsLogical) Chunk.EmitOp(val.LogicalValue ? OpCode.True : OpCode.False, 0);
                    else Chunk.EmitConstant(val, 0);
                    break;
                }

                case ThisExpression:
                    Chunk.EmitOp(OpCode.This, 0);
                    break;

                // ── Variables ──
                case VariableExpression v:
                {
                    var slot = ResolveLocal(v.Name);
                    if (slot != -1)
                    {
                        Chunk.EmitOp(OpCode.GetLocal, 0);
                        Chunk.EmitU16(slot, 0);
                    }
                    else
                    {
                        Chunk.EmitOp(OpCode.GetGlobal, 0);
                        Chunk.EmitU16(Chunk.AddConstant(WarValue.FromText(v.Name)), 0);
                    }
                    break;
                }

                // ── Assignment (as sub-expression) ──
                case AssignmentOperator a:
                    CompileAssignment(a, 0);
                    break;

                // ── Arithmetic ──
                case AdditionOperator a:
                    CompileExpression(a.Left);
                    CompileExpression(a.Right);
                    Chunk.EmitOp(OpCode.Add, 0);
                    break;

                case SubtractionOperator s:
                    CompileExpression(s.Left);
                    CompileExpression(s.Right);
                    Chunk.EmitOp(OpCode.Sub, 0);
                    break;

                case MultiplicationOperator m:
                    CompileExpression(m.Left);
                    CompileExpression(m.Right);
                    Chunk.EmitOp(OpCode.Mul, 0);
                    break;

                case DivisionOperator d:
                    CompileExpression(d.Left);
                    CompileExpression(d.Right);
                    Chunk.EmitOp(OpCode.Div, 0);
                    break;

                case ModuloOperator mo:
                    CompileExpression(mo.Left);
                    CompileExpression(mo.Right);
                    Chunk.EmitOp(OpCode.Mod, 0);
                    break;

                // ── Unary ──
                case NegateOperator n:
                    CompileExpression(n.Value);
                    Chunk.EmitOp(OpCode.Negate, 0);
                    break;

                case NotOperator no:
                    CompileExpression(no.Value);
                    Chunk.EmitOp(OpCode.Not, 0);
                    break;

                // ── Comparison ──
                case EqualsOperator e:
                    CompileExpression(e.Left);
                    CompileExpression(e.Right);
                    Chunk.EmitOp(OpCode.Equal, 0);
                    break;

                case NotEqualsOperator ne:
                    CompileExpression(ne.Left);
                    CompileExpression(ne.Right);
                    Chunk.EmitOp(OpCode.NotEqual, 0);
                    break;

                case LessThanOperator lt:
                    CompileExpression(lt.Left);
                    CompileExpression(lt.Right);
                    Chunk.EmitOp(OpCode.Less, 0);
                    break;

                case LessThanOrEqualToOperator le:
                    CompileExpression(le.Left);
                    CompileExpression(le.Right);
                    Chunk.EmitOp(OpCode.LessEqual, 0);
                    break;

                case GreaterThanOperator gt:
                    CompileExpression(gt.Left);
                    CompileExpression(gt.Right);
                    Chunk.EmitOp(OpCode.Greater, 0);
                    break;

                case GreaterThanOrEqualToOperator ge:
                    CompileExpression(ge.Left);
                    CompileExpression(ge.Right);
                    Chunk.EmitOp(OpCode.GreaterEqual, 0);
                    break;

                // ── Logical (short-circuit) ──
                case LogicalAndOperator la:
                {
                    CompileExpression(la.Left);
                    var endJump = Chunk.EmitJump(OpCode.JumpIfFalse, 0);
                    Chunk.EmitOp(OpCode.Pop, 0);
                    CompileExpression(la.Right);
                    Chunk.PatchJump(endJump);
                    break;
                }

                case LogicalOrOperator lo:
                {
                    CompileExpression(lo.Left);
                    var endJump = Chunk.EmitJump(OpCode.JumpIfTrue, 0);
                    Chunk.EmitOp(OpCode.Pop, 0);
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
                    foreach (var elem in arr.Values)
                        CompileExpression(elem);
                    Chunk.EmitOp(OpCode.NewArray, 0);
                    Chunk.EmitU16(arr.Values.Count, 0);
                    break;

                case ArrayValueOperator av:
                    CompileExpression(av.Left);
                    CompileExpression(av.Right);
                    Chunk.EmitOp(OpCode.IndexGet, 0);
                    break;

                case ArrayAppendOperator ap:
                    CompileExpression(ap.Left);
                    CompileExpression(ap.Right);
                    Chunk.EmitOp(OpCode.ArrayAppend, 0);
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
                    var typeName = ((VariableExpression)cast.Right).Name;
                    var nameIdx = Chunk.AddConstant(WarValue.FromText(typeName));
                    Chunk.EmitOp(OpCode.CastAs, 0);
                    Chunk.EmitU16(nameIdx, 0);
                    break;
                }

                case ClassInstanceOfOperator iof:
                {
                    CompileExpression(iof.Left);
                    var typeName = ((VariableExpression)iof.Right).Name;
                    var nameIdx = Chunk.AddConstant(WarValue.FromText(typeName));
                    Chunk.EmitOp(OpCode.InstanceOf, 0);
                    Chunk.EmitU16(nameIdx, 0);
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
                        Chunk.EmitOp(OpCode.NewNestedInstance, 0);
                        Chunk.EmitU16(nameIdx, 0);
                        Chunk.EmitByte((byte)nestedCe.PropertiesExpressions.Count, 0);
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
            foreach (var arg in call.ArgumentExpression)
                CompileExpression(arg);

            var nameIdx = Chunk.AddConstant(WarValue.FromText(call.Name));
            Chunk.EmitOp(OpCode.Call, 0);
            Chunk.EmitU16(nameIdx, 0);
            Chunk.EmitByte((byte)call.ArgumentExpression.Count, 0);
        }

        // ── Class instantiation: new ClassName[args] ──
        private void CompileClassInstantiation(ClassExpression ce)
        {
            foreach (var arg in ce.PropertiesExpressions)
                CompileExpression(arg);

            var nameIdx = Chunk.AddConstant(WarValue.FromText(ce.ClassName));
            Chunk.EmitOp(OpCode.NewInstance, 0);
            Chunk.EmitU16(nameIdx, 0);
            Chunk.EmitByte((byte)ce.PropertiesExpressions.Count, 0);
        }

        // ── Class property access: obj::prop, obj::method[args], obj::arr{i} ──
        private void CompileClassProperty(ClassPropertyOperator cp)
        {
            // ── Superinstruction: this :: prop → ThisGetProperty ──
            if (cp.Left is ThisExpression && cp.Right is VariableExpression thisVar)
            {
                var nameIdx = Chunk.AddConstant(WarValue.FromText(thisVar.Name));
                Chunk.EmitOp(OpCode.ThisGetProperty, 0);
                Chunk.EmitU16(nameIdx, 0);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), 0);
                return;
            }
            // ── Superinstruction: this :: arr{i} → ThisGetProperty + IndexGet ──
            if (cp.Left is ThisExpression && cp.Right is ArrayValueOperator thisArrOp
                && thisArrOp.Left is VariableExpression thisArrVar)
            {
                var nameIdx = Chunk.AddConstant(WarValue.FromText(thisArrVar.Name));
                Chunk.EmitOp(OpCode.ThisGetProperty, 0);
                Chunk.EmitU16(nameIdx, 0);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), 0);
                CompileExpression(thisArrOp.Right);
                Chunk.EmitOp(OpCode.IndexGet, 0);
                return;
            }

            CompileExpression(cp.Left); // push instance

            if (cp.Right is VariableExpression varExpr)
            {
                var nameIdx = Chunk.AddConstant(WarValue.FromText(varExpr.Name));
                Chunk.EmitOp(OpCode.GetProperty, 0);
                Chunk.EmitU16(nameIdx, 0);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), 0);
            }
            else if (cp.Right is FunctionExpression funcExpr)
            {
                // Method call: obj::method[args]
                foreach (var arg in funcExpr.ArgumentExpression)
                    CompileExpression(arg);
                var nameIdx = Chunk.AddConstant(WarValue.FromText(funcExpr.Name));
                Chunk.EmitOp(OpCode.CallMethod, 0);
                Chunk.EmitU16(nameIdx, 0);
                Chunk.EmitByte((byte)funcExpr.ArgumentExpression.Count, 0);
            }
            else if (cp.Right is ArrayValueOperator arrayOp && arrayOp.Left is VariableExpression arrayVar)
            {
                // obj::arr{i}  →  GetProperty then IndexGet
                var nameIdx = Chunk.AddConstant(WarValue.FromText(arrayVar.Name));
                Chunk.EmitOp(OpCode.GetProperty, 0);
                Chunk.EmitU16(nameIdx, 0);
                Chunk.EmitU16(Chunk.AllocCacheSlot(), 0);
                CompileExpression(arrayOp.Right);
                Chunk.EmitOp(OpCode.IndexGet, 0);
            }
        }
    }
}
