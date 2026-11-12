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
│   ├── StateHash.cs             # ComputeStateHash() encoding (FNV-1a over prints + user globals, plan §9.7)
│   ├── Audit/                   # T0.8 audit mode (temporary): AuditEvent/AuditKind, AuditReport; sink = WarScriptLanguage.AuditSink
│   ├── Token/
│   │   ├── Token.cs             # Token record (Type, Value, RowNumber)
│   │   ├── TokenType.cs         # Enum: Keyword, Variable, Operator, Numeric, Text, etc.
│   │   └── TokenStack.cs        # Peekable token stream with Back() support
│   ├── Parser/
│   │   ├── LexicalParser.cs     # Hand-written lexer/scanner
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
│   │   ├── OpCode.cs            # All VM opcodes (Call, CallValue, TailCall, etc.)
│   │   ├── Chunk.cs             # Bytecode buffer + constant pool + inline caches
│   │   ├── CompiledFunction.cs  # Compiled function + CallFrame struct
│   │   ├── Compiler.cs          # Single-pass AST→bytecode compiler
│   │   ├── WarVM.cs             # Stack-based bytecode VM ← HOTTEST FILE
│   │   ├── BytecodeSerializer.cs # Binary serialization (v1: MinArity, lambda constants, F64-raw numerics)
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
│   │   ├── Coroutine.cs         # Tree-walk coroutine (legacy)
│   │   └── BytecodeCoroutine.cs # VM-backed coroutine — owns its own WarVM
│   ├── Native/
│   │   ├── NativeHelper.cs      # Arg extraction (NumericArg→F64, IntArg→truncated int, TextArg, etc.)
│   │   ├── StringInterner.cs    # String dedup + pre-allocated "0".."999"
│   │   ├── ScriptRunner.cs      # Convenience runner for import system
│   │   ├── WarScriptLibraryRegistry.cs  # Central stdlib registration
│   │   └── Libraries/           # MathLibrary (F64; no random — D6), ArrayLibrary, CoroutineLibrary, UtilityLibrary
│   └── Exception/
│       └── SyntaxException.cs
└── Tests/
    ├── TestHelper.cs            # Run(name, source) and RunFile(resourceName)
    ├── Invariants.cs            # Post-run invariant checks + [KnownInvariantViolation] (checks in Invariants.Checks.cs)
    ├── Golden/GoldenRunner.cs   # Runs every Golden~/**/*.ws (source + bytecode × culture matrix)
    ├── Golden/GoldenFile.cs     # Directive parsing + one run; NUnit-free, shared with Headless~/Runner
    ├── Golden/IsolatedRun.cs    # '# isolate' runs in a child process + the runner's JSON protocol
    ├── ReproTests.cs            # Plan Appendix A repros that need C# (A.6, A.9, A.19, A.21), known-failing
    ├── DefaultParameterTests.cs / LambdaTests.cs / ConstEnumTests.cs
    ├── ExecutionTests.cs
    ├── BytecodeCoroutineTests.cs / BytecodeSerializationTests.cs
    ├── InstructionBudgetTests.cs / MemoryBudgetTests.cs / HotReloadTests.cs
    ├── ...
    ├── Golden~/                 # Golden .ws corpus (no .meta: Unity ignores ~ folders)
    │   ├── language/ deep/ algorithms/ gapfill/ regression/   # former assert-only resources
    │   └── repro/               # Plan Appendix A repros, mostly # known-failing
    └── resources/               # .ws files still driven by C# tests (RunFile, Benchmarks)
```

## Execution Pipeline

```
Source string → LexicalParser.Parse() → List<Token>
                                            ↓
                          StatementParser.Parse() → AST (with desugared defaults/const/enum)
                                            ↓
                          Compiler.CompileScript() → CompiledFunction (bytecode)
                                            ↓
                          WarVM.Run() / RunFunction() → execution
```

After compilation, the AST is discarded. Bytecode is the source of truth.

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
# Import: import "other_script"
# Builtins: print value, assert condition
# Numeric separators: 1_000_000
```

## Bytecode VM Architecture

Stack-based VM with fixed-size arrays (no heap allocation in hot path):
- **Value stack**: 1024 WarValue slots
- **Call frames**: 128 CallFrame slots (Function, IP, StackBase)
- **Exception handlers**: 32 TryHandler slots

Function call dispatch (3 paths):
| Target | Opcode | Resolution |
|---|---|---|
| Named function | `Call name arity` | DefinitionScope → MemoryScope fallback for globals |
| Local lambda variable | `GetLocal` + `CallValue arity` | Stack value → direct call |
| Class method | `CallMethod name arity` | Class DefinitionScope lookup |

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
- **Text literals come in three forms**: `"..."` and `$"..."` (identical — interpolation + escapes) and `"""..."""` (raw: no escapes, no interpolation, multi-line, drops the line breaks adjoining the delimiters). A raw literal closes on the *last* three quotes of a run, so its content may end in `"` but may not contain `"""`. `$"""` is rejected.
- **AST is discarded after compilation.** Bytecode is the source of truth.
- **`WarValue` is a struct.** Passed by value. Lambda function values stored as `NativeObject(CompiledFunction)`.
- **Default parameters**: `null` triggers the default (deliberate design — no way to pass "missing" vs "null").
- **Lambda limitations**: No closures (params + globals only). No postfix call syntax (`arr{0}[args]` — use temp variable). TCO disabled for local lambda calls.
- **Enums** are class instances. Members are properties. `name[]` is a method. Protected from reassignment via `ConstantNames`.
- **`[WsEnum]`/`[WsConst]` codegen** uses `GlobalMemoryScope.Set()` because `Register()` is called before `Run()` when the scope stack is empty.
- **BytecodeSerializer format version is 1.** Includes `MinArity` and `NativeObject(CompiledFunction)` (lambda) constants. Numeric constants serialize as the 64-bit **F64 raw** (`Numeric.Raw` / `FromRawNumeric`), replacing the old IEEE-754 `double` encoding — so bytecode produced before the fixed-point migration is **binary-incompatible despite sharing the version byte**; regenerate it from source.
- **`import` can start from bytecode.** `WarScriptLanguage.BytecodeResolver` (optional 5th ctor argument, `ScriptRunner.ImportScriptBytecode`) is asked for an imported path before `FileResolver` is; when it answers, the import is deserialized and run instead of lexed/parsed/compiled. A null answer, an unloadable one, or a throwing resolver falls back to the source (the last two also log through `Logger`). `const`/`enum` names in a bytecode-loaded import do not enter `ConstantNames` — same as `LoadBytecode()`.
- **Function lookup is by (name, argCount).** Default params register at all valid arities.
- **Exception boundary (T1.3)** is `WarVM.Execute`. A native that throws `ScriptErrorException(kind, message)` (`Runtime/Exception/ScriptErrorException.cs`) raises a script error with that message; any other exception from a native function *or* native method becomes the rescuable `"Native function/method '<name>' failed: <message>"`. An error no handler catches unwinds every VM frame (scopes and class contexts popped) before the host entry logs it. Any other C# exception inside the VM is a **host fault**: the VM restores the context depths recorded when it was entered, resets, and rethrows through every enclosing VM to the host entry (`Run`, `Call`, a coroutine resume), which logs `[WarScript] Host fault in '<script>': <exception with stack trace>`, records `LastHostFault` (internal) and returns — `Run()` no longer throws for it (a faulted coroutine ends). Exception: a `SyntaxException` from compiling an import still propagates out of `Run()`. Tests trigger a host fault with a throwing `DebugHook`.
- **Pending `ensure` work** is a stack in `WarVM` (`PendingEnsure`): an ensure block entered with an error to re-raise or a value to return. A `return` from the frame, an error caught by a handler outside the block, or unwinding the frame discards the record (so `raise`/`return` inside `ensure` replaces what was pending, CRASH-7).
- **Lexer caches globally** by source string. Call `LexicalParser.ClearCache()` for hot reload.

## Testing Conventions

- **Headless run (no Unity)** — `Headless~/` holds plain .NET projects over the same sources (Unity ignores `~` folders, so no `.meta` files there):
  ```bash
  ./Headless~/fetch-fixpointcs.sh                       # once; clones pinned FixPointCS into Headless~/external/
  dotnet test "Headless~/Tests/WarScript.Tests.csproj"  # whole suite (NUnit 3.14, net8.0, rolls forward)
  dotnet Headless~/Runner/bin/Debug/net8.0/WarScript.Runner.dll <file.ws> [--mode source|bytecode] [--culture name|invariant] [--budget n] [--memory-budget n]
  ```
  `Headless~/Runtime/WarScript.Runtime.csproj` compiles `Runtime/` + `Attributes/` + FixPointCS as `netstandard2.1` / C# 9.0 — the Unity constraint; never raise its `LangVersion`. To build against the game's own FixPointCS copy pass `-p:FixPointCSDir=<path>`. Test code must stay within the NUnit 3.5 API (no `ClassicAssert`, no `Assert.Multiple`) so it also runs in the Unity Test Runner. `Runtime/AssemblyInfo.cs` grants `InternalsVisibleTo("WarScript.Tests")` (same assembly name in both environments) `InternalsVisibleTo("WarScript.Runner")` and `InternalsVisibleTo("WarScript.Fuzz")`. `Headless~/Runner/` (plan §9.5) is a console app that runs one golden file once and prints `{"output", "status", "error", "invariants", "hash"}` JSON; exit code 0 = completed (any script status), 2 = invariant violated, 3 = C# exception escaped, anything else = crash. It compiles the NUnit-free test sources (`Tests/Golden/GoldenFile.cs`, `Tests/Golden/IsolatedRun.cs`, `Tests/Invariants.Checks.cs`), so keep NUnit out of those three files. A crash or escaped C# exception is reported with the WarScript method it happened in, as `(in <method>)`. The test project builds it first and finds it through `AssemblyMetadata("WarScriptRunner")`. `Headless~/Fuzz/` (T0.7, plan §9.6) is the source-mode fuzzer, not built by the tests: `dotnet run --project "Headless~/Fuzz/WarScript.Fuzz.csproj" -- --minutes 10 [--seed n] [--jobs n] [--cases n]` mutates `Tests/Golden~` files (skipping `# isolate` ones), runs each case through the runner (budget 1,000,000, memory budget 64 MB, 10 s timeout) and keeps the shortest case per failure signature in `Headless~/out/fuzz/<signature hash>/` plus `summary.txt`; `--case <seed>` regenerates one case (seeds depend on the corpus), `--replay <case.ws>` re-checks a saved one. Its `KnownFindings` table maps signatures to finding IDs (exit 4 = a signature matches none); remove an entry when its fix lands. `Headless~/ModTrace/` (T0.9) is a separate console app, not built by the tests: `WarScript.ModTrace <mod folder> [--out <folder>]` stubs every native a mod calls, runs each entry script and its functions, and writes per-script native-call traces plus `summary.txt` (default `Headless~/out/modtrace/`); `Documentation~/migration/BASELINE.md` holds the warman baseline it recorded (optional since plan DEC-20: no game baseline or game-session audit gates Phase 3).
- **Framework**: NUnit (`[TestFixture]`, `[Test]`, `Assert.AreEqual`)
- **Helper**: `TestHelper.Run("name", source)` → `(script, List<string> output)`
- **Helper**: `TestHelper.RunFile("filename.ws")` → loads from `Tests/resources/`
- **Golden files** (plan §9.2) — language behaviour is tested by default with a `.ws` file under `Tests/Golden~/<topic>/`; adding a test means adding one file, no C#. `GoldenRunner` runs each file from source and from bytecode (compile → `SaveBytecode` → `LoadBytecode` into a fresh instance) under Invariant, en-US, de-DE, sv-SE, fi-FI and tr-TR, with the full stdlib registered and the import resolver rooted at the file's folder. Every run must meet the expectations, hold the invariants, print the same lines and end with the same `ComputeStateHash()`. Each mode's invariant-culture hash is written to `Headless~/out/golden-hashes.<os>-<arch>.txt` (one `<file> [<mode>]: <hash>` line per run; overwritten by every run, so a filtered run writes a partial file); `./Headless~/compare-hashes.sh <a.txt> <b.txt>...` diffs such files from different machines. Directives are `#` comments (read from the lexer's Comment tokens, so `#` inside text is safe): `# expect: <line>` (whole printed output must equal the ordered list), `# expect-error: <Kind>[: <substring>]`, `# expect-compile-error: <substring>`, `# budget: n`, `# memory-budget: n`, `# known-failing: <ID>` (reported Ignored while it fails, fails once it passes), `# ignore-output`, `# no-stdlib`, `# modes: source`, `# isolate` (each run happens in a child process through `WarScript.Runner` with a 10 s timeout, so a stack overflow fails only that test; these cases are `[Category("HeadlessOnly")]` and Ignored inside Unity), `# expect-warning:` (always fails until T3.8). Until structured errors (T2.5) the error kind is recognised only from v1 messages that identify one (division/modulo by zero, budgets, assertion, stack overflows); other messages match any expected kind, and the error line is not checked.
- **Audit mode** (T0.8, plan §8.1, temporary until the migration is done): `WarScriptLanguage.AuditSink` receives an `AuditEvent` (kind, script, line, function, values) whenever the VM takes a v1 path whose behavior changes in v2; `AuditReport` groups them by (kind, script, line) and writes Markdown/JSON. Hooks are in `WarVM` behind `_script.AuditSink != null` checks; scope-based events use the internal `MemoryScope.Kind`, so a new place that creates a `MemoryScope` must pass its kind. `WARSCRIPT_AUDIT_REPORT=<file> dotnet test ...` writes the test-suite report (`Tests/AuditSuite.cs`); the committed one is `Documentation~/migration/audit-tests.md`, owner instructions in `Documentation~/migration/AUDIT.md`.
- **State hash** (`WarScriptLanguage.ComputeStateHash()`, plan §9.7): FNV-1a 64 over every line printed since the instance was created (fed through the internal `WarScriptLanguage.Print`, the only path `print` takes — keep it that way), `0xFF`, then the user globals (`UserMemoryScope`; host globals excluded) sorted ordinally. The byte layout is pinned by `StateHashTests.EncodingIsPinned`; changing it invalidates every recorded baseline (T0.9). v1 instances have no declared field list, so an instance hashes its constructor parameters in order and then its other fields by name — revisit when T3.4 adds fields.
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
