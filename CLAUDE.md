# CLAUDE.md — WarScript Codebase Guide

## What This Is

WarScript is a custom embeddable scripting language with a bytecode VM, written in C# (~11K LOC runtime, ~10K LOC tests). It ships as a Unity UPM package for scripting game logic without recompilation. Think "Lua for C#/Unity" with Ruby-inspired syntax.

Repository: `https://github.com/nine9123/WarScript.git#upm`

## Project Structure

```
WarScript/
├── Attributes/              # [WsModule], [WsFunction], [WsEnum], [WsConst] for codegen
│   └── WsAttributes.cs
├── Editor/                  # Unity editor tooling
│   └── WsBindingGenerator.cs   # Source generator: scans attributes → Register() methods
├── Runtime/
│   ├── WarScriptLanguage.cs     # Public API — the main entry point
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
│   │   ├── BytecodeSerializer.cs # Binary serialization (v2: MinArity + lambda constants)
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
│   │   ├── NativeHelper.cs      # Arg extraction helpers (NumericArg, TextArg, etc.)
│   │   ├── StringInterner.cs    # String dedup + pre-allocated "0".."999"
│   │   ├── ScriptRunner.cs      # Convenience runner for import system
│   │   ├── WarScriptLibraryRegistry.cs  # Central stdlib registration
│   │   └── Libraries/           # MathLibrary, ArrayLibrary, CoroutineLibrary, UtilityLibrary
│   └── Exception/
│       └── SyntaxException.cs
└── Tests/
    ├── TestHelper.cs            # Run(name, source) and RunFile(resourceName)
    ├── DefaultParameterTests.cs / LambdaTests.cs / ConstEnumTests.cs
    ├── ExecutionTests.cs / ComprehensiveLanguageTests.cs
    ├── BytecodeCoroutineTests.cs / BytecodeSerializationTests.cs
    ├── InstructionBudgetTests.cs / MemoryBudgetTests.cs / HotReloadTests.cs
    ├── ...
    └── resources/               # .ws script files (~70+)
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

# Types: Numeric (double), Logical (true/false), Text, Array, Class, Null, NativeObject
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
        return Math_sqrt [this :: x * this :: x + this :: y * this :: y]
    end
end
p = new Point [3, 4]

# Inheritance (including multiple): class Dog [name] : Animal [name] end
# Casting: obj as Animal     Type check: obj is Animal

# Exception handling: begin / rescue err / ensure / end
# String interpolation: "Hello {name}, you are {age} years old"
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
    [WsFunction("my_func")] public static double MyFunc(double a, string b) => a + b.Length;
    [WsEnum] public enum State { Idle, Moving, Attacking }
    [WsConst] public const int MAX_UNITS = 50;
}
```

Run **WarScript → Generate Bindings**. The generator produces `Register()` that handles functions (auto-marshaling), enums (class instances with `::` access, `name[]`, `values/names/count`), and consts (immutable globals via `GlobalMemoryScope`).

## Scope System

- **DefinitionScope**: Function and class definitions. Indexed by `(name, argCount)` — supports multi-arity for default params.
- **MemoryScope**: Variable values at runtime. Dict-based with parent chain lookup.
- **ConstantNames**: Global `HashSet<string>` on `WarScriptLanguage`. Parser rejects reassignment. Populated by `const`/`enum`/`[WsConst]`/`[WsEnum]`. Cleared on `Reload()`/`LoadBytecode()`.

## Existing Desugar Patterns

1. **String interpolation** — Lexer: `"hello {x}"` → `"hello " + (x)` tokens
2. **Default parameters** — Parser: `fun f [a, b = 1]` → inject `if b == null then b = 1` body prefix. Multi-arity registration. VM null-pads stack.
3. **Compound assignment** — Expression reader: `x += 1` → `x = x + 1`
4. **Constants** — Parser: `const X = 5` → assignment + `ConstantNames.Add()`. Parse-time immutability.
5. **Enums** — Parser: `enum E ... end` → class definition + member properties + `name[]` method (if-chain) + `values`/`names`/`count` arrays + singleton instance
6. **Lambda** — Expression reader + compiler: `fun [x] ... end` → `CompiledFunction` constant in parent's pool. `CallValue` opcode for local calls.

## Key Invariants & Gotchas

- **`{` and `}` are array delimiters**, not block delimiters. Blocks end with `end`. Array indexing is `arr{i}`. Function args use `[`, `]`.
- **`::` is the property access operator** — `obj :: name`, `this :: x`, `DamageType :: PHYSICAL`.
- **AST is discarded after compilation.** Bytecode is the source of truth.
- **`WarValue` is a struct.** Passed by value. Lambda function values stored as `NativeObject(CompiledFunction)`.
- **Default parameters**: `null` triggers the default (deliberate design — no way to pass "missing" vs "null").
- **Lambda limitations**: No closures (params + globals only). No postfix call syntax (`arr{0}[args]` — use temp variable). TCO disabled for local lambda calls.
- **Enums** are class instances. Members are properties. `name[]` is a method. Protected from reassignment via `ConstantNames`.
- **`[WsEnum]`/`[WsConst]` codegen** uses `GlobalMemoryScope.Set()` because `Register()` is called before `Run()` when the scope stack is empty.
- **BytecodeSerializer format version is 2.** Includes `MinArity` in function definitions and handles `NativeObject(CompiledFunction)` constants for lambda serialization.
- **Function lookup is by (name, argCount).** Default params register at all valid arities.
- **Lexer caches globally** by source string. Call `LexicalParser.ClearCache()` for hot reload.

## Testing Conventions

- **Framework**: NUnit (`[TestFixture]`, `[Test]`, `Assert.AreEqual`)
- **Helper**: `TestHelper.Run("name", source)` → `(script, List<string> output)`
- **Helper**: `TestHelper.RunFile("filename.ws")` → loads from `Tests/resources/`
- **Pattern**: Check print output or rely on `assert` in .ws scripts

## Performance-Sensitive Areas

- `WarVM.Execute()` — the main dispatch loop. Avoid allocations and branching.
- `MemoryScope.Get()`/`.Set()` — every global variable access. Dict lookup + parent chain.
- `InlineCache` on property access — avoids string hashing on repeated `::` access.
- `StringInterner` — reduces GC pressure for short strings. Pre-allocated "0".."999".
- `CallValue` has same frame setup cost as `Call` — no additional overhead.
