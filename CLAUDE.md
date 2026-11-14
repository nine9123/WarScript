# CLAUDE.md — WarScript Codebase Guide

## What This Is

WarScript is a custom embeddable scripting language with a bytecode VM, written in C# (~11K LOC runtime, ~10K LOC tests). It ships as a Unity UPM package for scripting game logic without recompilation. Think "Lua for C#/Unity" with Ruby-inspired syntax.

Repository: `https://github.com/nine9123/WarScript.git#upm`

**v2 work in progress.** The WarScript v2 design decisions and phased implementation plan (Phases 0–3: tests, crash/determinism fixes, host API v2, language v2) live in `Documentation~/WARSCRIPT_V2_PLAN.md`. Task IDs such as `T0.1` and decision IDs such as `DEC-5` in commit messages refer to that document.

## Determinism & Fixed-Point Numerics (READ FIRST)

WarScript is **deterministic**: it is the scripting layer for a lockstep-networked game, so identical inputs must produce bit-identical results on every machine and platform (x86-64, ARM; Windows, macOS, Linux/SteamDeck). Floating point cannot guarantee that, so **the language has no `double`/`float` anywhere**.

- **Every WarScript number is a `FixMath.F64`** — a signed 32.32 fixed-point value (64-bit raw). `WarValue.Numeric` is `F64`; `WarValue.FromNumeric`, `NativeHelper.NumericArg`, and every numeric operator are F64. There is no `double`/`float` in `Runtime/`.
- **WarScript references FixPointCS, it does not vendor it.** `WarScript.Runtime.asmdef` references the `FixPointCS` assembly by name and keeps `noEngineReferences: true`. The consuming Unity project must provide FixPointCS — it is not declared in `package.json` `dependencies` and is not copied into this package.
- **No nondeterminism sources in Runtime:** no `System.Random`, no `UnityEngine` types, no wall-clock. `MathLibrary`'s `random*` functions were removed (D6); deterministic randomness is supplied by the engine as a separate module.

### What this means when working in the codebase

- **Numeric literals** (`LexicalParser` + `Parser/NumericLiteral.cs`): the grammar is `'-'? digit+ ('.' digit+)?`, so fractional literals like `1.0`, `0.5`, `99.5` ARE valid and are parsed to exact F64 **using integer arithmetic only** (`(frac << 32) / den`) — never touching float/double, so the raw is bit-identical cross-platform. "Integer-only" describes the *parsing*, not a restriction to whole numbers. Values exactly representable in 32.32 (`0.5`, `99.5`) round-trip exactly; others (`0.7`) are deterministically truncated. Fractional precision past 9 digits is truncated; integer-part range is ±2147483647. Malformed literals throw `SyntaxException` (D2a) — no silent coercion.
- **`F64` → `int` truncates** (`NativeHelper.IntArg`, the generator's `int` marshal). To round, call `F64.RoundToInt(...)`. **`F64` has no conversion operator to/from `int`/`float`/`double`** — `(int)someF64` does NOT compile; use `RoundToInt` / `F64.FromInt`. F64↔int *arithmetic and comparison* operators do exist (so `someF64 == 3` is fine).
- **Transcendentals are approximate.** `MathLibrary` `sqrt`, `pow`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan2`, `lerp`, `pi`, etc. are FixPointCS fixed-point implementations: deterministic but not bit-exact to real math (`pow[2,10] != 1024`, `sqrt[4] != 2` exactly). `round` is **half-up**, not banker's rounding. `sqrt` and `pow` check their own domain before calling FixPointCS (`sqrt[n<0]` → 0, `pow[b<=0, e!=0]` → 0, `pow[b, 0]` → 1) — FixPointCS itself only returns 0 for those inputs in a player build and routes them through the invalid-argument handler (which throws) in the editor.
- **`F64Vec3` is an opaque `NativeObject`** handle in WarScript (D5) — scripts pass it around but don't introspect components from script.
- **Formatting is culture-invariant and integer-only (T1.6, plan 5.10).** `WarValue.FormatNumber` prints integers as invariant digits and fractions as the shortest ≤9-digit decimal that `NumericLiteral.TryParseRaw` reads back to the same raw, else the exact expansion (`1/3` → `0.33333333325572311878204345703125`). Native objects never go through the host's `ToString()`: `F64Vec3` → `(x, y, z)`, function values → `<fun name>` (lambdas `<fun lambda>`), others → `<TypeName>` or a formatter registered with `WarScriptLanguage.RegisterNativeFormatter<T>` (applied by the VM's text conversions — `print`, `+`, interpolation — via `WarValue.Format(NativeFormatters)`). Any number→text conversion in `Runtime/` must pass `CultureInfo.InvariantCulture` (interpolations with numeric holes: `FormattableString.Invariant`); `FormattingTests.Runtime_HasNoCultureDependentToString` scans for parameterless `ToString()` calls and keeps an allow-list of non-number receivers. String comparisons in `Runtime/` are ordinal: `StartsWith`/`EndsWith`/`IndexOf`/`LastIndexOf` with a text argument, `string.Compare` and `ToUpper`/`ToLower` must name `StringComparison.Ordinal` (or an invariant culture); `FormattingTests.Runtime_HasNoCultureSensitiveStringOperations` scans for them. A registered native formatter that throws `ScriptErrorException` raises a rescuable error; any other exception from it is a host fault.
- **Divide / modulo by zero raise a catchable exception** (`"Division by zero"` / `"Modulo by zero"`), not a crash (D4) — handle with `begin/rescue`.
- **Coroutine timing** (`yield wait <seconds>`) uses an F64 `dt` supplied by the host each tick (D7); there is no internal clock.

## Project Structure

```
WarScript/
├── Attributes/              # [WsModule], [WsFunction], [WsEnum], [WsConst] for codegen
│   └── WsAttributes.cs
├── Editor/                  # Unity editor tooling
│   └── WsBindingGenerator.cs   # Source generator: scans attributes → Register() methods
├── Runtime/
│   ├── WarScriptLanguage.cs     # Public API — the main entry point
│   ├── CompileResult.cs         # T2.2: Compile() result — CompileResult, CompileDiagnostic, DiagnosticSeverity
│   ├── ScriptResult.cs          # T2.3: entry results — ScriptResult, ScriptStatus, ScriptError, ScriptFrame
│   ├── FunctionHandle.cs        # T2.6: GetFunction() result; re-resolves by (name, arity) after Reload
│   ├── StateHash.cs             # ComputeStateHash() encoding (FNV-1a over prints + user globals, plan §9.7)
│   ├── Audit/                   # T0.8 audit mode (temporary): AuditEvent/AuditKind, AuditReport; sink = WarScriptLanguage.AuditSink
│   ├── Token/
│   │   ├── Token.cs             # Token record (Type, Value, RowNumber, Column)
│   │   ├── TokenType.cs         # Enum: Keyword, Variable, Operator, Numeric, Text, etc.
│   │   └── TokenStack.cs        # Peekable token stream with Back() support
│   ├── Parser/
│   │   ├── LexicalParser.cs     # Hand-written lexer/scanner
│   │   ├── TokenCache.cs        # T1.9: bounded, locked LRU of token lists (the lexer cache)
│   │   ├── ParsedModule.cs      # T3.3: ParseModule() result (imports, top level, definitions) + ParseBody
│   │   └── StatementParser.cs   # Recursive descent → AST + desugar logic (const/enum/defaults)
│   ├── Expression/
│   │   ├── IExpression.cs       # All expressions implement this
│   │   ├── ExpressionReader.cs  # Shunting-yard expression parser (handles lambda in expr position)
│   │   ├── LambdaExpression.cs  # Lambda/anonymous function AST node
│   │   ├── Operator/            # One class per operator (AdditionOperator, etc.)
│   │   │   ├── Operator.cs      # Operator enum with precedence groups
│   │   │   └── Extensions/
│   │   │       └── OperatorExtension.cs  # String→Operator, precedence, factory
│   │   └── Value/
│   │       ├── WarValue.cs      # Tagged union struct — THE core type
│   │       ├── ClassData.cs     # Runtime class instance data
│   │       ├── ConstantExpression.cs
│   │       └── ThisExpression.cs
│   ├── Statement/               # AST statement nodes
│   │   ├── CompositeStatement.cs    # Block of statements
│   │   ├── ConditionStatement.cs    # if/elif/else
│   │   ├── FunctionStatement.cs
│   │   ├── ClassStatement.cs
│   │   ├── Loop/                    # ForLoop, IterableLoop, WhileLoop, Break, Next
│   │   ├── HandleExceptionStatement.cs  # begin/rescue/ensure
│   │   ├── ImportStatement.cs
│   │   ├── YieldStatement.cs
│   │   └── ...
│   ├── Bytecode/
│   │   ├── OpCode.cs            # All VM opcodes, explicit values (never renumber or reuse one)
│   │   ├── OpInfo.cs            # Opcode table (operands, stack effects) + BytecodeFormat (Version, Hash)
│   │   ├── Chunk.cs             # Bytecode buffer + constant pool + inline caches
│   │   ├── CompiledFunction.cs  # Compiled function + CallFrame struct
│   │   ├── Compiler.cs          # Single-pass AST→bytecode compiler
│   │   ├── WarVM.cs             # Stack-based bytecode VM ← HOTTEST FILE
│   │   ├── BytecodeSerializer.cs # Binary serialization (format v3: version + format hash header, run-length lines; hardened loader)
│   │   ├── BytecodeVerifier.cs  # Load-time/compile-time verifier (plan 10.3) → MaxStack
│   │   └── DebugContext.cs      # Source-map debugger (StepMode, DebugHook, StackEntry)
│   ├── Context/
│   │   ├── MemoryScope.cs       # Variable storage (dict-based, parent chain lookup)
│   │   ├── MemoryContext.cs     # Scope stack manager with object pooling
│   │   ├── Definition/
│   │   │   ├── DefinitionScope.cs       # Holds function + class defs (multi-arity index)
│   │   │   ├── FunctionDefinition.cs    # Function def (AST + compiled form)
│   │   │   ├── NativeFunctionDefinition.cs  # C# lambda-backed function
│   │   │   ├── ClassDefinition.cs       # Class def (details, base types, scope)
│   │   │   ├── ClassDetails.cs          # Name, constructor args, property indices
│   │   │   └── FunctionDetails.cs       # Name + argument list + MinArity
│   │   └── ...
│   ├── Coroutine/
│   │   ├── ICoroutine.cs        # Interface: Id, IsComplete, IsReady(dt), Resume()
│   │   └── BytecodeCoroutine.cs # VM-backed coroutine — owns its own WarVM
│   ├── Native/
│   │   ├── NativeHelper.cs      # Arg extraction (NumericArg→F64, IntArg→truncated int, TextArg, etc.); failures throw NativeArgumentException
│   │   ├── StringInterner.cs    # String dedup + pre-allocated "0".."999"
│   │   ├── ScriptRunner.cs      # Convenience runner for import system
│   │   ├── WarScriptLibraryRegistry.cs  # Central stdlib registration
│   │   └── Libraries/           # MathLibrary (F64; no random — D6), ArrayLibrary, CoroutineLibrary, UtilityLibrary
│   └── Exception/
│       ├── SyntaxException.cs
│       └── CompileError.cs      # T1.8: a SyntaxException for compiler limits (Reason, ScriptName, Line)
├── Tools~/WarScript.Migrate/  # T3.2: one-time v1 → v2 migration tool (console app + Migrator.cs + its tests)
└── Tests/
    ├── TestHelper.cs            # Run(name, source) and RunFile(resourceName)
    ├── Invariants.cs            # Post-run invariant checks + [KnownInvariantViolation] (checks in Invariants.Checks.cs)
    ├── Golden/GoldenRunner.cs   # Runs every Golden~/**/*.ws (source + bytecode × culture matrix)
    ├── Golden/GoldenFile.cs     # Directive parsing + one run; NUnit-free, shared with Headless~/Runner
    ├── Golden/IsolatedRun.cs    # '# isolate' runs in a child process + the runner's JSON protocol
    ├── StructuredErrorTests.cs  # T2.5: kinds, lines, call stacks (nested entries, ensure, natives)
    ├── ReproTests.cs            # Plan Appendix A repros that need C# (A.6, A.9, A.19, A.21), known-failing
    ├── DefaultParameterTests.cs / LambdaTests.cs / ConstEnumTests.cs
    ├── ExecutionTests.cs
    ├── BytecodeCoroutineTests.cs / BytecodeSerializationTests.cs
    ├── InstructionBudgetTests.cs / MemoryBudgetTests.cs / HotReloadTests.cs
    ├── ...
    ├── Golden~/                 # Golden .ws corpus (no .meta: Unity ignores ~ folders)
    │   ├── language/ deep/ algorithms/ gapfill/ regression/   # former assert-only resources
    │   ├── errors/              # T2.5: one golden per error kind, with its line
    │   └── repro/               # Plan Appendix A repros, mostly # known-failing
    └── resources/               # .ws files still driven by C# tests (RunFile, Benchmarks)
```

## Execution Pipeline

```
(WarScriptLanguage.Compile(): idempotent, cached CompileResult, never throws for script errors — T2.2)
Source string → LexicalParser.Parse() → IReadOnlyList<Token>
                                            ↓
                          StatementParser.Parse() → AST (with desugared defaults/const/enum)
                                            ↓
                          Compiler.CompileScript() → CompiledFunction (bytecode)
                                            ↓
                          WarVM.Run() / RunFunction() → execution
```

After compilation, the AST is discarded. Bytecode is the source of truth.

## Host API at a Glance (Phase 2, T2.9)

```csharp
var script = new WarScriptLanguage(name, source, fileResolver, logger /*, bytecodeResolver */);
WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);   // natives/bindings before compiling
script.InstanceLabel = "unit-42";                // optional: on every ScriptError and runtime log line
CompileResult c = script.Compile();              // optional; never throws for script errors
ScriptResult r = script.Run();                   // top-level code; Value null
FunctionHandle tick = script.GetFunction("tick", 1);           // null if undefined; survives Reload
r = script.Call(tick, WarValue.FromNumeric(dt));               // or Call(tick, ReadOnlySpan<WarValue>)
r = script.StartCoroutine("patrol", args, false, out int id);  // runs to the first yield
r = script.TickCoroutines(dt);                   // Ok: Value = active coroutine count
if (!r.IsOk) { /* r.Status: Error / Halted / HostFault; r.Error: ScriptError (Kind, Line, CallStack) */ }
```

Entry points (`Run`, `Call`, `StartCoroutine`, `TickCoroutines`) return `ScriptResult` and never throw for a script error; an unhandled error or host fault at the outermost entry halts the instance (`IsHalted`, `Fault`, `Faulted`, logged unless `LogUnhandledErrors` is false) until `ClearFault()` or `Reload()`. `Reload` (`SyntaxException`) and `LoadBytecode` (`InvalidDataException`) throw. Budgets reset per outermost entry. The user-facing version of this is the "Host Integration (C#)" section of `README.md`; keep the two in step when the API changes.

## WarScript Language Syntax — Quick Reference

```ruby
# Variables (dynamically typed, no declaration keyword)
x = 42
name = "hello"

# Types: Numeric (F64 — 32.32 fixed-point, NOT double), Logical (true/false), Text, Array, Class, Null, NativeObject
# Arithmetic: + - * / %     String ops: + (concat), - (remove), * (repeat)
# Comparison: == != < <= > >=     Logical: and or !
# Assignment: = += -= *= /=

# Arrays — {} for literals, {index} for access
arr = {1, 2, 3}
arr{0}           # → 1
arr << 4         # append

# Conditionals: if / elif / else / end
if x > 0
    print "positive"
end

# Loops: while, for-range, for-range-step, foreach
loop i in 0..10         # for (exclusive upper bound)
loop i in 0..100 by 5   # with step
loop item in arr         # foreach
loop x > 0              # while

# Functions — args in [], called with []
fun add [a, b]
    return a + b
end
result = add [3, 4]

# Default parameters — trailing params can have = expr
fun greet [name, greeting = "Hello"]
    return greeting + ", " + name
end

# Named arguments
create [team: "Red", name: "Soldier", hp: 100]

# Lambda / first-class functions — fun [...] ... end in expression position
double = fun [x] return x * 2 end
apply [{1, 2, 3}, fun [x] return x * 10 end]

# Constants — immutable globals
const MAX_HP = 100

# Enums — class-based, access with ::
enum DamageType
    PHYSICAL
    MAGICAL
    TRUE = 5
end
DamageType :: PHYSICAL              # → 0
DamageType :: name [0]              # → "PHYSICAL"
DamageType :: values                # → {0, 1, 5}
DamageType :: names                 # → {"PHYSICAL", "MAGICAL", "TRUE"}
DamageType :: count                 # → 3
loop v in DamageType :: values      # iterate all members

# Classes — constructor args in [], property access with ::
class Point [x, y]
    fun magnitude []
        return sqrt [this :: x * this :: x + this :: y * this :: y]   # MathLibrary names are unprefixed
    end
end
p = new Point [3, 4]

# Inheritance (including multiple): class Dog [name] : Animal [name] end
# Casting: obj as Animal     Type check: obj is Animal

# Exception handling: begin / rescue err / ensure / end
# String interpolation: "Hello {name}, you are {age} years old"   (or explicitly $"Hello {name}")
# String escapes: \" \\ \{ \} \n \t \r   — anything else is a SyntaxException
# Raw strings (no escapes, no interpolation, multi-line): """print "{x}""""  — for embedded source
# Coroutines: yield, yield wait 2.0, yield until condition
# Import: import "other_script"   — only at the top of a file, before every other statement (T3.3)
# Fields: @x is this :: x (lexed as those tokens, T3.3); not in lambdas
# global a, b — in a function/method/lambda/class body only; `global` is reserved (T3.3)
# Function/method/lambda bodies (T3.4): an assigned name is a local of the whole body; globals are
#   read freely, written only after `global name`; a method's bare names are never fields (use @x)
# Builtins: print value, assert condition
# Numeric separators: 1_000_000
```

## Bytecode VM Architecture

Stack-based VM with fixed-size arrays (no heap allocation in hot path):
- **Value stack**: 1024 WarValue slots
- **Call frames**: 128 CallFrame slots (Function, IP, StackBase, `FrameKind`: Script / Function / Method / Constructor — what the frame pushed on the context stacks, popped by `PopFrameContexts`)
- **Exception handlers**: 32 TryHandler slots

Function call dispatch (3 paths):
| Target | Opcode | Resolution |
|---|---|---|
| Named function | `Call name arity` | DefinitionScope → MemoryScope fallback for globals |
| Local lambda variable | `GetLocal` + `CallValue arity` | Stack value → direct call |
| Class method | `CallMethod name arity` | Class DefinitionScope lookup |
| Constructor | `NewInstance` / `NewNestedInstance` | Allocation step (whole instance graph, no code run), then one `Constructor` frame per body on the same VM (T1.1) |

Other key features: superinstructions (fused compare+jump, this+property), inline caching, tail call optimization (disabled for local lambda targets), instruction/memory budgets, source-map debugger, coroutine suspend/resume.

## How To Add a New Native Function

### Option 1: Manual

```csharp
scope.AddFunction(new NativeFunctionDefinition(
    new FunctionDetails("my_func", new List<string> { "a", "b" }),
    args => WarValue.FromNumeric(NativeHelper.NumericArg(args, 0) + NativeHelper.TextArg(args, 1).Length),
    "Description", "NumericValue"));
```

### Option 2: Attribute-based codegen

```csharp
[WsModule("my_module")]
public static partial class MyModule
{
    [WsFunction("my_func")] public static F64 MyFunc(F64 a, string b) => a + b.Length;
    [WsEnum] public enum State { Idle, Moving, Attacking }
    [WsConst] public const int MAX_UNITS = 50;
}
```

Run **WarScript → Generate Bindings**. The generator produces `Register()` that handles functions (auto-marshaling), enums (class instances with `::` access, `name[]`, `values/names/count`), and consts (immutable globals via `GlobalMemoryScope`).

**Binding type contract (enforced at generation time).** Marshalable parameter/return types are: `F64` (→ `NumericArg`), `int` (→ truncating `IntArg`), `bool`, `string`, `WarValue` (passthrough), `List<WarValue>` (array), and `F64Vec3` (opaque NativeObject). `double`/`float` are **rejected** — a single one throws `InvalidOperationException` and aborts the whole `Generate Bindings` run (use `F64`). An `F64` parameter cannot carry a C# compile-time default, so for an optional numeric take a `WarValue x = default` and read `x.IsNumeric ? x.Numeric : fallback` (an omitted argument arrives as `Null`). `[WsConst]` `float`/`double` values are baked to a fixed-point raw at generation time (no float literal reaches the generated code).

## Scope System

- **DefinitionScope**: Function and class definitions. Indexed by `(name, argCount)` — supports multi-arity for default params.
- **MemoryScope**: Variable values at runtime. Dict-based with parent chain lookup.
- **ConstantNames**: Global `HashSet<string>` on `WarScriptLanguage`. Parser rejects reassignment. Populated by `const`/`enum`/`[WsConst]`/`[WsEnum]`. Cleared on `Reload()`/`LoadBytecode()`.

## Existing Desugar Patterns

1. **String interpolation** — Lexer: `"hello {x}"` → `"hello " + (x)` tokens. `$"..."` is the same path. Escapes are folded into the `Text` token at lex time; `"""..."""` skips both and emits one verbatim `Text` token.
2. **Default parameters** — Parser: `fun f [a, b = 1]` → inject `if b == null then b = 1` body prefix. Multi-arity registration. VM null-pads stack.
3. **Compound assignment** — Expression reader: `x += 1` → `x = x + 1`
4. **Constants** — Parser: `const X = 5` → assignment + `ConstantNames.Add()`. Parse-time immutability.
5. **Enums** — Parser: `enum E ... end` → class definition + member properties + `name[]` method (if-chain) + `values`/`names`/`count` arrays + singleton instance
6. **Lambda** — Expression reader + compiler: `fun [x] ... end` → `CompiledFunction` constant in parent's pool. `CallValue` opcode for local calls.

## Key Invariants & Gotchas

- **`{` and `}` are array delimiters**, not block delimiters. Blocks end with `end`. Array indexing is `arr{i}`. Function args use `[`, `]`.
- **`::` is the property access operator** — `obj :: name`, `this :: x`, `DamageType :: PHYSICAL`.
- **Frontend v2 (T3.3).** The lexer turns `@name` into `this`, `::`, `name`, all at the `@`'s column (a keyword or literal word after `@` is a `SyntaxException`), and lexes `global` as a `Keyword`. `StatementParser.ParseModule` returns a `ParsedModule` (`Imports`, `TopLevel`, `Definitions`; the imports are also the first top-level statements, since they still run when reached until T3.7); `Compile` and runtime imports use it. The parser tracks the body it is in through `WarScriptLanguage.ParsingBody` (`ParseBody`: TopLevel / Function / Method / Lambda / ClassBody, kept by nested blocks) and rejects: `import` anywhere but before every other top-level statement, `global` at the top level (or `global` with no names), `yield` in a class body (PROP-21), `this`/`@x` in a lambda (PROP-16). `GlobalStatement` (`Names`) is checked by the compiler (T3.4, see below); in a class body it still compiles to nothing. `WarScriptLanguage.ParsingV1` (set only by `ParseForMigration`) turns those checks off and hands `global` to the parser as a name, so the migration tool keeps reading v1 scripts.
- **Text literals come in three forms**: `"..."` and `$"..."` (identical — interpolation + escapes) and `"""..."""` (raw: no escapes, no interpolation, multi-line, drops the line breaks adjoining the delimiters). A raw literal closes on the *last* three quotes of a run, so its content may end in `"` but may not contain `"""`. `$"""` is rejected.
- **Explicit compile step (T2.2).** `Compile()` lexes (the constructor no longer does), parses and compiles, and caches its `CompileResult` (`Success` + `CompileDiagnostic`s: severity, message, script name, line — `CompileError.Line`, else 0 — column 0) until `Reload` replaces the source; `LoadBytecode` sets it to success. A `SyntaxException` becomes an error diagnostic; any failed compile clears the definitions and restores the `ConstantNames` the parser had added to. `Run`, `Call` and `StartCoroutine` compile when needed and return a failed compile as an `Error` result of kind `CompileError` (nothing run, no `BeginEntry`; it halts the instance); `Reload`, `SaveBytecode` and `CompileWithoutRunning` go through `EnsureCompiled`, which throws the cached `SyntaxException`. `StartCoroutine` before `Run` works (the top-level code has not run).
- **Result types (T2.3).** `Run`, `Call`, `StartCoroutine(name, args, loop, out id)` and `TickCoroutines` return a `ScriptResult` (`Status` Ok/Error/Halted/HostFault, `Value`: the return value, `Run`: null, `TickCoroutines`: active count; `Error`: a `ScriptError` with `Kind`, `Message`, `Value`, `ScriptName`, `Line`, `CallStack`, `HostException`, internal `CompileException`). Build an entry's result with `EntryResult(value)` *before* `EndEntry` (which clears the error at the outermost entry, keeping it as `ExceptionContext.LastUnhandled`), and return it through `FinishEntry(result)` *after* `EndEntry`; a nested entry's error result leaves the error raised for the calling script (PROP-1). `ScriptErrorKind` is in namespace `WarScript` (`ScriptErrorException` stays in `WarScript.Exception`). **Structured errors (T2.5):** every raise has a kind (`WarVM.RuntimeError(kind, message)`; the message→kind table is plan T2.5; `raise` is `Raised`, `NativeHelper` throws `NativeArgumentException` — ArgumentError/TypeError, v1 message naming the native). `WarVM.DoHandleException` first calls `CaptureCallStack`, which records the VM's frames (innermost first, line of `IP - 1`, script `Chunk.Source`) on the raised `Exception.Exception` before anything unwinds; an error a nested entry left raised gets the calling VM's frames appended (`StackOwner` tells a re-raise in the same VM apart), and an ensure block re-raises the same `Exception.Exception` (`PendingEnsure.Error`, `ExceptionContext.Reraise`), so kind and stack survive. `ScriptError.Line`/`ScriptName` are the innermost frame's; an error raised outside any frame has line 0 and no stack. Never raise without going through `DoHandleException` (directly or via the end-of-instruction `IsRaised` check), or the error has no stack. `StartCoroutine` also has a `ReadOnlySpan<WarValue>` overload (T2.7; the array one forwards to it, and a coroutine copies its arguments). The `int StartCoroutine(name, args, loop = false)` overload remains (used by `start_coroutine`). `TestHelper.Run` rethrows a `CompileError` result's `CompileException` so v1-style `Assert.Throws<SyntaxException>` tests keep working.
- **Fault and halt model (T2.4, plan 6.3/6.4).** `FinishEntry` halts the instance when an outermost entry (or a compile error / refused entry at depth 0) ends with `Error` or `HostFault`: it clears the coroutines, sets `IsHalted` and `Fault`, logs `[WarScript] ` + `ScriptError.ToString()` (plus the C# exception for a host fault) unless `LogUnhandledErrors` is false, then invokes `Faulted` synchronously. A halted instance answers `Run`, `Call`, `StartCoroutine` and `TickCoroutines` (and a coroutine resume) with `Halted` (`Error` = the fault) before compiling or `BeginEntry`. `ClearFault()` and `Reload()` un-halt; `LoadBytecode` does not. A coroutine's own unhandled error ends it (a looping one too) and, nested (started or ticked by a native), stays raised for the calling script (PROP-1). `TickCoroutines` walks a snapshot of the list newest first, skips coroutines stopped during the tick (`_coroutinesRemoved` counts stops/clears) and stops at the first non-Ok resume (PROP-3); completed ones are dropped afterwards. **Instance label (T2.8, PROP-4):** `InstanceLabel` (host-set, default null) is copied into every `ScriptError` at creation (pass it to the `ScriptError` factories) and prefixes every runtime log line as `[label] ` — send runtime log lines through the internal `WarScriptLanguage.Log(line)`, never `Logger` directly; only `print` output (`WarScriptLanguage.Print`) bypasses it. Tests: `Tests/InstanceLabelTests.cs`. Tests that re-enter an instance after an error must `ClearFault()`; tests that compare printed output either set `LogUnhandledErrors = false` (golden runner, `ReproTests`) or remove `"[WarScript] " + script.Fault`. Tests: `Tests/FaultTests.cs`.
- **AST is discarded after compilation.** Bytecode is the source of truth. AST nodes are plain data (T2.1): no `Execute()`/`Evaluate()`, no tree-walk interpreter, no `HaltFlags`; `Call` on a function without bytecode (a native) returns a TypeError result (`Cannot call ...: it has no bytecode`), `StartCoroutine` on one returns one with id -1.
- **`WarValue` is a struct.** Passed by value. Lambda function values stored as `NativeObject(CompiledFunction)`.
- **Default parameters**: `null` triggers the default (deliberate design — no way to pass "missing" vs "null").
- **Lambda limitations**: No closures (own params and locals + globals; reading a local of the enclosing code is a compile error, T3.4). No postfix call syntax (`arr{0}[args]` — use temp variable). TCO disabled for local lambda calls.
- **Enums** are class instances. Members are properties. `name[]` is a method. Protected from reassignment via `ConstantNames`.
- **`[WsEnum]`/`[WsConst]` codegen** uses `GlobalMemoryScope.Set()` because `Register()` is called before `Run()` when the scope stack is empty.
- **Bytecode format version is 7 (T1.7, T3.4, T3.5).** The header is `WSBC`, `BytecodeFormat.Version` (u8) and `BytecodeFormat.Hash` (u32, FNV-1a over the `OpInfo` table, the version and `BytecodeFormat.Layout`); the loader accepts only an exact match (R4), so any older bytecode must be regenerated from source. Every `OpCode` has an explicit value; removed values (7–8 `PushScope`/`PopScope`, 58 `YieldUntil`, 61–65 the unused fused compare-jumps) are never reused. `OpInfo.Table` is the one description of each opcode's operands and stack effect: the disassembler decodes through it (unknown or truncated code prints as such, never throws), the VM's dispatch `default:` is a host fault ("Invalid opcode"). Changing an opcode, an operand or what the serializer writes means: update the table/`Layout`, bump `Version`, and record the new `Hash` (`BytecodeFormatTests.FormatHash_MatchesTheOpcodeTable` prints it). `BytecodeFormatTests.CompiledGolden_*` checks every function compiled from `Tests/Golden~` against the table. `BytecodeVerifier.Verify` (plan 10.3) checks every function the loader reads and everything the compiler produces, and sets `CompiledFunction.MaxStack`: operands and targets against the table, stack heights from height = arity (rescue at handler height + 1, with one more handler open when the block has an ensure block; ensure at handler height), handlers open per frame (never below 0, equal at merges, none at a `TailCall`), no path off the end; at load also `MaxStack` ≤ 1,024 and arity ≤ 255. The VM checks `base + MaxStack` (plus any arguments beyond the arity, which stay in the frame) against the 1,024-slot stack before pushing any frame and raises the rescuable "Value stack overflow" for the caller — there is no `IndexOutOfRangeException` catch, so never push outside what the verifier models. The loader (10.4) throws only `InvalidDataException` for malformed input (truncation, oversized counts, nesting > 32, bad UTF-8, verifier rejections), and `LoadBytecode` installs nothing unless the whole file loads. A compiler change that the verifier rejects is a compiler bug: fix the compiler, don't relax the verifier. Version 3 (T1.7) replaced v2's one `int` line per code byte with a run-length `LineTable` (`Chunk.Lines`: an (offset, line) entry where the line changes; `GetLine(offset)`), in memory and in the file; the verifier checks it starts at 0, increases and stays inside the code. Version 4 (T1.7, Q-14) ends the rescue body of a block with an ensure block with a `PopHandler`: the VM runs such a rescue body under an ensure-only handler (the block's handler with its rescue cleared), so leaving it by `return`, `raise` or an error runs the ensure block (SEM-13), and its normal end, `break` or `next` removes that handler. A block without an ensure block (`ensure` IP = end IP) holds no handler in its rescue body, so recursion from it isn't limited by the 32 handler slots. Version 5 (T3.4) changed what function bodies mean, not the opcode table: a function, method or lambda keeps every variable in a stack slot and its `SetGlobal` writes a declared global (see Name resolution). Version 6 (T3.4) did the same for class bodies (fields written with `ThisSetProperty`, `SetGlobal` a declared global) and stopped emitting `PushScope`/`PopScope`; version 7 (T3.5, first slice) removed those two opcodes and the VM's scope-depth bookkeeping. The plan's "format v3" (T3.11) will take the next number (8). The record content is otherwise v1's: `MinArity`, `NativeObject(CompiledFunction)` (lambda) constants, numerics as the 64-bit F64 raw.
- **`import` can start from bytecode.** `WarScriptLanguage.BytecodeResolver` (optional 5th ctor argument, `ScriptRunner.ImportScriptBytecode`) is asked for an imported path before `FileResolver` is; when it answers, the import is deserialized and run instead of lexed/parsed/compiled. A null answer, an unloadable one, or a throwing resolver falls back to the source (the last two also log through `Logger`). `const`/`enum` names in a bytecode-loaded import do not enter `ConstantNames` — same as `LoadBytecode()`.
- **Name resolution (T3.4).** `Compiler.BeginBody` starts every function, method and lambda body: the parameters take the first slots, then `BodyNames.Collect` (the pre-pass) lists every name the body assigns without declaring it `global` (nested blocks included, lambdas and nested definitions excluded) and each gets a slot for the whole body, pushed as `Null` right after the parameters, in order of first assignment; loop and rescue variables still get their slot when their block starts (`Local.Kind`: Hidden / Parameter / BodyLocal / Block). Compile errors (`CompileError`, at the name's position): reading a body local before its first assignment in the source (`_assignedSoFar`; not checked in an ensure body compiled again where `break`/`next` leaves it, `_inlinedEnsure`), a loop/rescue variable named like a parameter, body local, declared global or enclosing loop/rescue variable, `global x` after a use of `x` in the body (`_usedNames`) or for a parameter, and a lambda reading a stack slot of the code around it (`_enclosingLocals`, "closures are not supported"; calls are not checked; a name the lambda declares `global` is a global, `_declaredGlobals`). Reads of other names are `GetGlobal`; writes of declared globals are `SetGlobal`, which the VM sends to `UserMemoryScope` when the current scope is a `FunctionLocal`/`MethodLocal` one (creating the name there). Blocks have no runtime scope (DEC-4; T3.5 removed `PushScope`/`PopScope`): a top-level `if`/loop/`begin` writes globals like the rest of the top level (A.17), so a function's call scope is always the current one, and always empty. A method's scope has `UserMemoryScope` as parent (not the instance's fields), so bare reads in a method see globals (PROP-20, A.16) and fields need `@x`. **Class bodies** (constructor frames, `Compiler._classBody`): a bare assignment of a name not declared `global` compiles to `ThisSetProperty` (declares a field of the instance), a declared global to `SetGlobal`, which the VM sends to `UserMemoryScope` from a `ClassInstance` scope too; a bare read is `GetGlobal`, and the instance's `ClassInstance` scope has `UserMemoryScope` as parent, so it finds a field first, then a global (5.7, SEM-4). `global` of a constructor parameter is a compile error. **Field check:** `Compiler.ClassFields` collects a class's fields (constructor parameters, names its body assigns bare or as `@x = ...`, minus declared globals, plus its bases' fields, `BodyNames.Collect(..., classBody: true)`); `@x`/`this :: x` (read, write, `@x{i}`) in a method or the class body naming anything else is a `CompileError` `'x' is not a field of class 'C'` (`CheckField`; A.18a). The check is skipped (null field set) when a base class is not found in the definition scopes at compile time (an import's), has no source (host-registered, bytecode-loaded) or shares its name with a nested class of the deriving class (the VM resolves bases from the `new` site's scope). **One field scope per instance (Q-16):** every class part of an instance shares one `ClassData.MemoryScope` (created by `WarVM.AllocateInstance` for the derived class; each part's `PropertyValues` are its own parameters' references in it), so a field any class body assigns is seen by every class's methods. A base parameter whose name is already a field bound to a different argument (`class C [p, q] : Sub [q, p], Mul [p, q]` with `Sub [a, b]`, `Mul [a, b]`) is a `CompileError` `'a' is bound twice in class 'C': ...` (`Compiler.CheckParameterBindings`, only where `ClassFields` is not null), else the same message as a rescuable TypeError at `new` (`WarVM.BoundTwiceMessage`); passing the same parameter on, a diamond, or two parameters both given no argument are one field. **Not yet:** the field list with indices in `ClassDetails`; the program/instance data split for F-VM-3. The test corpus was migrated with `Tools~/WarScript.Migrate --rewrite` (except the four files whose rewrite needs `Text_remove`, T3.6). The audit kinds `GlobalWriteFromFunction`, `FieldAccessViaBareName`, `GlobalShadowedInClass` and `BlockVariableEscapes` can no longer occur (`DynamicPropertyCreated` only from outside a class or where the field check is skipped).
- **Function lookup is by (name, argCount).** Default params register at all valid arities.
- **Function handles (T2.6).** `GetFunction(name, arity)` returns a `FunctionHandle` (`Runtime/FunctionHandle.cs`: `Name`, `Arity`, `Instance`, `IsDefined`; internal `Definition`), or null when nothing of that name and arity is defined now. `Call(FunctionHandle, ReadOnlySpan<WarValue>)` (T2.7: copies the span onto the VM stack, allocation-free with a reused buffer) and `Call(FunctionHandle, params WarValue[])`, which forwards to it, are the only `Call`s: the handle re-resolves through `_definitionScope` whenever `WarScriptLanguage.DefinitionsVersion` changed (every `DefinitionScope.AddFunction` and `Clear` of the instance's scopes increments it — `Reload`, `LoadBytecode`, imports, host `AddFunction`), so handles survive a reload; a handle whose function is gone is a refused entry with NameError (`Function 'f' with N args is not defined`), a handle of another instance throws `ArgumentException`. Tests: `Tests/FunctionHandleTests.cs`.
- **Exception boundary (T1.3)** is `WarVM.Execute`. A native that throws `ScriptErrorException(kind, message)` (`Runtime/Exception/ScriptErrorException.cs`) raises a script error with that message; any other exception from a native function *or* native method becomes the rescuable `"Native function/method '<name>' failed: <message>"`. An error no handler catches unwinds every VM frame (scopes and class contexts popped) before the host entry logs it. Any other C# exception inside the VM is a **host fault**: the VM restores the context depths recorded when it was entered, resets, and rethrows through every enclosing VM to the host entry (`Run`, `Call`, a coroutine resume). The outermost entry records `LastHostFault` (internal) and returns a `HostFault` result, which halts the instance (T2.4; a faulted coroutine ends). A nested entry instead records the fault as pending and returns the `HostFault` result to its native; the VM that called the native rethrows it (`WarScriptLanguage.ThrowPendingHostFault`, after every native body, in `RaiseNativeFailure` and after a native formatter runs, `WarVM.Text`), so every enclosing entry stops too and a native cannot let the script continue. A `SyntaxException` from compiling an import propagates through nested entries and becomes a `CompileError` result at the outermost one. Tests trigger a host fault with a throwing `DebugHook`.
- **Constructors are frames (T1.1).** `new` first allocates the whole instance (`AllocateInstance`: derived and base-class `ClassData`, shared `ValueReference`s, properties from the arguments; a class on its own base path raises the rescuable `"Class 'X' inherits from itself"`), then `EnterConstructors` pushes the instance as the expression value and one `FrameKind.Constructor` frame per constructor body in reverse execution order (bases depth-first in `BaseTypes` order, then the derived class). Constructor bodies therefore share the VM's budgets and `FramesMax` (deep recursion → rescuable `"Stack overflow"`, raised before any frame is pushed when they don't all fit), an error in one unwinds to the code that executed `new`, and a constructor's return value is discarded. Never create a `WarVM` to run script code from inside the VM — imports still do until Phase 3 (they share the instance budget since T1.5).
- **Host entries and budgets (T1.5).** `Run`, `Call` and `BytecodeCoroutine.Resume` are host entries: `WarScriptLanguage.BeginEntry`/`EndEntry` count `EntryDepth`, and only the outermost entry resets `Budget` (`ExecutionBudget`: instructions left + memory used, shared by every VM of the instance, so callbacks, imports, constructors and script-started coroutines draw from it). A native that calls back into its own instance gets a new `WarVM` when the cached one is busy (`AcquireVM`; `WarVM.IsRunning` — never `Reset()` a running VM). More than `MaxHostNesting` (32) nested entries raise the rescuable `"Host nesting limit exceeded"` for the calling native. A nested entry's unhandled error stays raised and continues from the native call site (PROP-1); only the outermost entry logs it. While such an error is raised, a further nested entry runs nothing (`BeginEntry` returns false), so a later callback cannot rescue it and a native cannot suppress it (6.5). An exhausted instruction budget stays exhausted until the next outermost entry (BUDGET-2): every further instruction raises the same error again (`ExecutionBudget.Exhausted`, so its line and call stack stay where the budget ran out), and rescue/ensure bodies don't run.
- **Allocation guards (T1.4).** `WarScriptLanguage.MaxTextLength` / `MaxArrayLength` (default 1,048,576 each, PROP-5). Text `+`, `*` and index writes raise the rescuable `"Text too long"`; array `+`, `<<`, literals and `Array_insert` raise `"Array too long"`. Sizes are computed in `long` and checked *before* allocating; `TrackAlloc` (memory budget) is likewise called before the allocation and its `false` must be handled at once (`if (!TrackAlloc(..)) { if (DoHandleException(..)) break; return; }`). `text - ""` returns the text unchanged.
- **Value depth (T1.2).** `WarValue.MaxValueDepth` (256, PROP-5; also used by `StateHash`) bounds how deep arrays/instances nest for `Equals`, `GetHashCode` and `ToString`. Formatting prints a cycle or anything deeper as `[...]`; equality deeper than the limit throws `WarScriptLimitException` → rescuable `"Value nested too deeply"` (so `a == b` on cyclic arrays is an error, not a crash); hashing looks at the count plus at most 8 elements, 3 levels deep. Shared arrays (`a = {a, a}` repeated) have exponentially many paths (BUDGET-3): equality memoizes the pairs it found equal (`WarValue.EqualityMemo`, which still raises the depth error where the full walk would), and formatting stops after `WarValue.MaxFormattedLength` (1,048,576) characters, writing `...` for the rest. `ComputeStateHash()` is still exponential on them (Q-13), so goldens must not leave such values in globals. Keep the depth/path as parameters, never static state.
- **`break`/`next` out of `begin` (Q-12, CRASH-8 fixed).** The compiler tracks the `begin` blocks it is inside (`Compiler.TryCtx`: begin, rescue or ensure body). A `break`/`next` leaves each block opened inside its loop, innermost first: pops scopes/locals to the block's entry, then `PopHandler` (from the begin body) or `DropPending` (from the ensure body: discards its pending re-raise/return), then inlines the ensure body unless already in it. Afterwards it restores the compiler's locals/scope depth/`begin` stack, since the unreachable code after it still compiles inside the block (SEM-12).
- **Pending `ensure` work** is a stack in `WarVM` (`PendingEnsure`): an ensure block entered with an error to re-raise or a value to return. A `return` from the frame, an error caught by a handler outside the block, or unwinding the frame discards the record (so `raise`/`return` inside `ensure` replaces what was pending, CRASH-7). A rescue body runs under an ensure-only handler when its block has an ensure block (Q-14), so its error or `return` enters the ensure block like a begin body's.
- **Compiler limits (T1.8).** What the format can't encode is a `CompileError` (a `SyntaxException` with `Reason`, `ScriptName`, `Line`), never a truncated operand: code, jumps and handler addresses past 65,535 bytes ("function too large (bytecode exceeds 65,535 bytes)"), a U16 operand past 65,535, a 65,536th constant, more than 255 arguments in a call or `new`, more than 255 parameters, an array literal of more than 65,535 elements. `Chunk` raises them with line 0; `Compiler.CompileStatement` fills in the statement's line. Emit a count byte through `EmitArgCount`, pops through `Chunk.EmitPops` (splits `PopN` at 255), handler addresses through `Chunk.PatchAddress` — never `(byte)` or hand-written U16 patches.
- **Columns (T3.1).** `Token.Column` counts from 1 (0 = unknown) in UTF-16 code units, so a tab is one column; `\r\n`, `\n` and a lone `\r` each end one line. Tokens desugared around an interpolation hole carry the column of its `{`. Every `SyntaxException` has `ScriptName`, `Line`, `Column` (`Locate`/`LocateIn` set them only when still unknown, so the innermost site wins): throw with a position when a token is at hand (`new SyntaxException(message, line, column)`); otherwise `StatementParser.ParseExpression` reports it at the start of its statement, and `Compiler.CompileStatement` at the statement being compiled (`_column`). `ScriptError.SourceLine` + a caret line appear in `ToString()`. Runtime columns: `BinaryOperatorExpression`, `UnaryOperatorExpression`, `FunctionExpression` and `ClassExpression` are `IPositionedExpression`s (`Line`, `Column` of their operator/name/index `{` token, set by `ExpressionReader`); `Compiler.CompileExpression` moves `_column` to one that is on the line being compiled (`SetColumn`, which also sets `Chunk.Column`), and every emitted byte is marked with it in the in-memory `LineTable` (an entry per change of line *or* column). `CaptureCallStack` puts it in each `ScriptFrame`; `ScriptError.Column` is the innermost frame's, and its `SourceLine` is shown for an error in the instance's own script. The file's line table still has no columns (the serializer writes one entry per change of line; loaded code has column 0) until format v3 (T3.11).
- **Line information (T1.10).** Expressions carry no line, so the compiler emits everything with `Compiler._line`, the line of the statement being compiled (set and restored by `CompileStatement`; a desugared statement without a line keeps the enclosing one; a function, constructor or lambda starts at its definition's line). Never pass a literal `0` as an emit line: only a function's implicit closing `Null`, `Return` has line 0 (`BytecodeFormatTests.CompiledGolden_EveryInstructionHasALine` checks every golden). A multi-line expression reports its statement's first line until T3.1 adds columns.
- **Lexer caches globally** by source string (T1.9): `TokenCache`, a locked LRU of `LexicalParser.CacheCapacity` (64) entries, shared by every instance and thread; `Parse` returns a read-only `IReadOnlyList<Token>`. Call `LexicalParser.ClearCache()` for hot reload.
- **No static mutable state in `Runtime/` (R1, T1.9).** A static field must be `readonly` or `const`, and a `readonly` static array or collection must be on the allow-list in `Tests/StaticStateTests.cs` with its reason (initialized once and only read, synchronized, or thread-static). Per-instance state goes on `WarScriptLanguage`.

## Testing Conventions

- **Headless run (no Unity)** — `Headless~/` holds plain .NET projects over the same sources (Unity ignores `~` folders, so no `.meta` files there):
  ```bash
  ./Headless~/fetch-fixpointcs.sh                       # once; clones pinned FixPointCS into Headless~/external/
  dotnet test "Headless~/Tests/WarScript.Tests.csproj"  # whole suite (NUnit 3.14, net8.0, rolls forward)
  dotnet Headless~/Runner/bin/Debug/net8.0/WarScript.Runner.dll <file.ws|file.wsbc> [--mode source|bytecode] [--culture name|invariant] [--budget n] [--memory-budget n]
  ```
  `Headless~/Runtime/WarScript.Runtime.csproj` compiles `Runtime/` + `Attributes/` + FixPointCS as `netstandard2.1` / C# 9.0 — the Unity constraint; never raise its `LangVersion`. To build against the game's own FixPointCS copy pass `-p:FixPointCSDir=<path>`. Test code must stay within the NUnit 3.5 API (no `ClassicAssert`, no `Assert.Multiple`) so it also runs in the Unity Test Runner. `Runtime/AssemblyInfo.cs` grants `InternalsVisibleTo("WarScript.Tests")` (same assembly name in both environments) `InternalsVisibleTo("WarScript.Runner")`, `InternalsVisibleTo("WarScript.Fuzz")` and `InternalsVisibleTo("WarScript.Migrate")`. `Headless~/Runner/` (plan §9.5) is a console app that runs one golden file once and prints `{"output", "status", "error", "invariants", "hash"}` JSON (a script error is `{"kind", "line", "message"}`) (a `.wsbc` argument is bytecode loaded as it is; a file the loader rejects has status `rejected`); exit code 0 = completed (any script status), 2 = invariant violated, 3 = C# exception escaped, anything else = crash. It compiles the NUnit-free test sources (`Tests/Golden/GoldenFile.cs`, `Tests/Golden/IsolatedRun.cs`, `Tests/Invariants.Checks.cs`), so keep NUnit out of those three files. A crash or escaped C# exception is reported with the WarScript method it happened in, as `(in <method>)`. The test project builds it first and finds it through `AssemblyMetadata("WarScriptRunner")`. `Headless~/Fuzz/` (T0.7, plan §9.6) is the fuzzer (source mode; bytecode mode with `--bytecode`), not built by the tests: `dotnet run --project "Headless~/Fuzz/WarScript.Fuzz.csproj" -- --minutes 10 [--seed n] [--jobs n] [--cases n]` mutates `Tests/Golden~` files (skipping `# isolate` ones), runs each case through the runner (budget 1,000,000, memory budget 64 MB, 10 s timeout) and keeps the shortest case per failure signature in `Headless~/out/fuzz/<signature hash>/` plus `summary.txt`; `--case <seed>` regenerates one case (seeds depend on the corpus), `--replay <case.ws>` re-checks a saved one. Its `KnownFindings` table maps signatures to finding IDs (exit 4 = a signature matches none); remove an entry when its fix lands. `--bytecode` is the bytecode mode (T1.7): the corpus is compiled once in-process, each case flips 1–8 bytes after the 9-byte header or truncates the file, and runs as a `.wsbc` file through the runner (outcome `rejected` = `InvalidDataException` at load, acceptable); output defaults to `Headless~/out/fuzz-bytecode/`, `--case <seed>` needs `--bytecode` again, `--replay` takes a `.wsbc`. `Headless~/ModTrace/` (T0.9) is a separate console app, not built by the tests: `WarScript.ModTrace <mod folder> [--out <folder>]` stubs every native a mod calls, runs each entry script and its functions, and writes per-script native-call traces plus `summary.txt` (default `Headless~/out/modtrace/`); `Documentation~/migration/BASELINE.md` holds the warman baseline it recorded (optional since plan DEC-20: no game baseline or game-session audit gates Phase 3). `Tools~/WarScript.Migrate/` (T3.2, plan 8.2; temporary, deleted once every script is migrated) is the v1 → v2 migration tool: `dotnet run --project "Tools~/WarScript.Migrate" -- (--report | --rewrite) <root>... [--out <dir>] [--report-file <file>] [--host-globals a,b] [--host-globals-file <file>]` parses every `.ws` file with the v1 frontend (`WarScriptLanguage.ParseForMigration`, which records each expression's token span into `ExpressionSpans` — null in a normal parse — and keeps the AST; `VariableExpression` has `Line`/`Column` for it), takes files connected by imports as one module set, applies the plan 8.2 rules (`global` declarations, `@field`, `Text_remove`, import moves, `global` → `global_`; report-only items) and writes a Markdown report (exit 2 when a file does not parse). Its analysis (`Migrator.cs`) and tests (`Tests/MigratorTests.cs`) are compiled into the headless test project, which also builds the console app; `Documentation~/migration/migration-report.md` is its `--report` run over `Tests/Golden~` and `Tests/resources`.
- **Framework**: NUnit (`[TestFixture]`, `[Test]`, `Assert.AreEqual`)
- **Helper**: `TestHelper.Run("name", source)` → `(script, List<string> output)`
- **Helper**: `TestHelper.RunFile("filename.ws")` → loads from `Tests/resources/`
- **Golden files** (plan §9.2) — language behaviour is tested by default with a `.ws` file under `Tests/Golden~/<topic>/`; adding a test means adding one file, no C#. `GoldenRunner` runs each file from source and from bytecode (compile → `SaveBytecode` → `LoadBytecode` into a fresh instance) under Invariant, en-US, de-DE, sv-SE, fi-FI and tr-TR, with the full stdlib registered and the import resolver rooted at the file's folder. Every run must meet the expectations, hold the invariants, print the same lines and end with the same `ComputeStateHash()`. Each mode's invariant-culture hash is written to `Headless~/out/golden-hashes.<os>-<arch>.txt` (one `<file> [<mode>]: <hash>` line per run; overwritten by every run, so a filtered run writes a partial file); `./Headless~/compare-hashes.sh <a.txt> <b.txt>...` diffs such files from different machines. Directives are `#` comments (read from the lexer's Comment tokens, so `#` inside text is safe): `# expect: <line>` (whole printed output must equal the ordered list), `# expect-error: <Kind>[: <substring>]`, `# expect-compile-error: <substring>`, `# budget: n`, `# memory-budget: n`, `# known-failing: <ID>` (reported Ignored while it fails, fails once it passes), `# ignore-output`, `# no-stdlib`, `# modes: source`, `# isolate` (each run happens in a child process through `WarScript.Runner` with a 10 s timeout, so a stack overflow fails only that test; these cases are `[Category("HeadlessOnly")]` and Ignored inside Unity), `# expect-warning:` (always fails until T3.8). The error kind must match exactly (T2.5), and when `# expect-error:` trails a code line (`x = 1 / 0   # expect-error: DivisionByZero`) the error's line must be that line; on a line of its own any line is accepted. `Tests/Golden~/errors/` has one golden per error family.
- **Audit mode** (T0.8, plan §8.1, temporary until the migration is done): `WarScriptLanguage.AuditSink` receives an `AuditEvent` (kind, script, line, function, values) whenever the VM takes a v1 path whose behavior changes in v2; `AuditReport` groups them by (kind, script, line) and writes Markdown/JSON. Hooks are in `WarVM` behind `_script.AuditSink != null` checks; scope-based events use the internal `MemoryScope.Kind`, so a new place that creates a `MemoryScope` must pass its kind. `WARSCRIPT_AUDIT_REPORT=<file> dotnet test ...` writes the test-suite report (`Tests/AuditSuite.cs`); the committed one is `Documentation~/migration/audit-tests.md`, owner instructions in `Documentation~/migration/AUDIT.md`.
- **State hash** (`WarScriptLanguage.ComputeStateHash()`, plan §9.7): FNV-1a 64 over every line printed since the instance was created (fed through the internal `WarScriptLanguage.Print`, the only path `print` takes — keep it that way), `0xFF`, then the user globals (`UserMemoryScope`; host globals excluded) sorted ordinally. The byte layout is pinned by `StateHashTests.EncodingIsPinned`; changing it invalidates every recorded baseline (T0.9). An instance hashes its constructor parameters in order and then its other fields (base-class fields included, one field scope per instance since Q-16) by name — revisit when the indexed field list lands.
- **C# repros** (`Tests/ReproTests.cs`): Appendix A repros that need a native or a host value. Each check returns null for the v2 behaviour or a deviation string, and `KnownFailing("<ID>", failure)` reports it Ignored until fixed and fails once it passes — the C# counterpart of `# known-failing:`.
- **Pattern**: Check print output or rely on `assert` in .ws scripts
- **Invariants** (`Tests/Invariants.cs`, plan §9.3): every `TestHelper.Run`/`RunFile` checks afterwards that `MemoryContext`/`DefinitionContext` depths are back where they were, no exception is left raised, `ClassInstanceContext` is empty and the VM is idle (`WarVM.IsIdle`, unless the script yielded). A test that currently breaks one carries `[KnownInvariantViolation("<finding/task ID>")]`: the violation is logged instead of failing, the test's own asserts still run, and the marker fails the test once nothing is violated any more. No test carries one today (T1.3 fixed the 15 that did).
- **Comparing numerics**: `WarValue.Numeric` is `F64`. Compare against `F64.FromInt(n)` (the `==(F64,int)` operator also works), or project with `F64.RoundToInt(...)` — there is no `(int)F64` cast. For approximate results (sqrt/pow/trig, or non-representable values like `0.7`), assert on `.Float` with a tolerance: `Assert.AreEqual(1.3f, v.Float, 0.001f)`.

## Performance-Sensitive Areas

- `WarVM.Execute()` — the main dispatch loop. Avoid allocations and branching.
- `MemoryScope.Get()`/`.Set()` — every global variable access. Dict lookup + parent chain.
- `InlineCache` on property access — avoids string hashing on repeated `::` access.
- `StringInterner` — reduces GC pressure for short strings. Pre-allocated "0".."999".
- `CallValue` has same frame setup cost as `Call` — no additional overhead.
