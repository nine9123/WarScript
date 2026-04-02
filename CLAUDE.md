# CLAUDE.md — WarScript Codebase Guide

## What This Is

WarScript is a custom embeddable scripting language with a bytecode VM, written in C# (~10K LOC runtime, ~8K LOC tests). It ships as a Unity UPM package for scripting game logic without recompilation. Think "Lua for C#/Unity" with Ruby-inspired syntax.

Repository: `https://github.com/nine9123/WarScript.git#upm`

## Project Structure

```
WarScript/
├── Attributes/              # [WsModule], [WsFunction] etc. for codegen bindings
│   └── WsAttributes.cs
├── Editor/                  # Unity editor tooling
│   └── WsBindingGenerator.cs   # Source generator: [WsModule] → Register() methods
├── Runtime/
│   ├── WarScriptLanguage.cs     # Public API — the main entry point (618 LOC)
│   ├── Token/
│   │   ├── Token.cs             # Token record (Type, Value, RowNumber)
│   │   ├── TokenType.cs         # Enum: Keyword, Variable, Operator, Numeric, Text, etc.
│   │   └── TokenStack.cs        # Peekable token stream with Back() support
│   ├── Parser/
│   │   ├── LexicalParser.cs     # Hand-written lexer/scanner (373 LOC)
│   │   └── StatementParser.cs   # Recursive descent → AST (379 LOC)
│   ├── Expression/
│   │   ├── IExpression.cs       # All expressions implement this
│   │   ├── ExpressionReader.cs  # Shunting-yard expression parser (293 LOC)
│   │   ├── Operator/            # One class per operator (AdditionOperator, etc.)
│   │   │   ├── Operator.cs      # Operator enum with precedence groups
│   │   │   └── Extensions/
│   │   │       └── OperatorExtension.cs  # String→Operator, precedence, factory
│   │   └── Value/
│   │       ├── WarValue.cs      # Tagged union struct — THE core type (224 LOC)
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
│   │   ├── OpCode.cs            # All VM opcodes (115 LOC)
│   │   ├── Chunk.cs             # Bytecode buffer + constant pool + inline caches (222 LOC)
│   │   ├── CompiledFunction.cs  # Compiled function + CallFrame struct
│   │   ├── Compiler.cs          # Single-pass AST→bytecode compiler (1156 LOC)
│   │   ├── WarVM.cs             # Stack-based bytecode VM (1732 LOC) ← HOTTEST FILE
│   │   ├── BytecodeSerializer.cs # Binary serialization of compiled bytecode
│   │   └── DebugContext.cs      # Source-map debugger (StepMode, DebugHook, StackEntry)
│   ├── Context/
│   │   ├── MemoryScope.cs       # Variable storage (dict-based, parent chain lookup)
│   │   ├── MemoryContext.cs     # Scope stack manager with object pooling
│   │   ├── Definition/
│   │   │   ├── DefinitionScope.cs       # Holds function + class definitions
│   │   │   ├── FunctionDefinition.cs    # Function def (AST + compiled form)
│   │   │   ├── NativeFunctionDefinition.cs  # C# lambda-backed function
│   │   │   ├── ClassDefinition.cs       # Class def (details, base types, scope)
│   │   │   ├── ClassDetails.cs          # Name, constructor args, property indices
│   │   │   └── FunctionDetails.cs       # Name + argument list
│   │   ├── ExceptionContext.cs
│   │   ├── ReturnContext.cs / BreakContext.cs / NextContext.cs
│   │   ├── ClassInstanceContext.cs
│   │   └── ValueReference.cs    # Boxed reference wrapper for mutable variables
│   ├── Coroutine/
│   │   ├── ICoroutine.cs        # Interface: Id, IsComplete, IsReady(dt), Resume()
│   │   ├── Coroutine.cs         # Tree-walk coroutine (legacy, 203 LOC)
│   │   └── BytecodeCoroutine.cs # VM-backed coroutine — owns its own WarVM (129 LOC)
│   ├── Native/
│   │   ├── NativeHelper.cs      # Arg extraction helpers (NumericArg, TextArg, etc.)
│   │   ├── StringInterner.cs    # String dedup + pre-allocated "0".."999"
│   │   ├── ScriptRunner.cs      # Convenience runner for import system
│   │   ├── WarScriptLibraryRegistry.cs  # Central stdlib registration
│   │   └── Libraries/
│   │       ├── MathLibrary.cs       # pow, sqrt, floor, ceil, clamp, lerp, etc.
│   │       ├── ArrayLibrary.cs      # remove_at, contains, index_of, pop, insert, etc.
│   │       ├── CoroutineLibrary.cs  # coroutine_start, coroutine_stop, etc.
│   │       └── UtilityLibrary.cs    # General helpers
│   └── Exception/
│       └── SyntaxException.cs
└── Tests/
    ├── TestHelper.cs            # Run(name, source) and RunFile(resourceName)
    ├── ExecutionTests.cs        # Core language feature tests (NUnit)
    ├── ComprehensiveLanguageTests.cs
    ├── BytecodeCoroutineTests.cs
    ├── InstructionBudgetTests.cs
    ├── SourceMapDebuggerTests.cs
    ├── HotReloadTests.cs
    ├── LexerTests.cs / ParserTests.cs
    ├── ...                      # ~20 test files
    └── resources/               # .ws script files executed by tests
        ├── class_creation.ws
        ├── test_functions.ws
        ├── test_loops.ws
        └── ...                  # ~70 .ws test scripts
```

## Execution Pipeline

```
Source string → LexicalParser.Parse() → List<Token>
                                            ↓
                          StatementParser.Parse() → AST (CompositeStatement tree)
                                            ↓
                          Compiler.CompileScript() → CompiledFunction (bytecode)
                                            ↓
                          WarVM.Run() / RunFunction() → execution
```

After compilation, the AST is discarded. Bytecode is the source of truth.
The lexer caches by source string. The compiler is single-pass.

## WarScript Language Syntax — Quick Reference

```ruby
# Variables (dynamically typed, no declaration keyword)
x = 42
name = "hello"

# Types: Numeric (double), Logical (true/false), Text, Array, Class, Null, NativeObject

# Arithmetic: + - * / % (also: ** exponent, // floor div via operators)
# String ops: + (concat), - (remove), * (repeat)
# Comparison: == != < <= > >=
# Logical: and or !
# Assignment: = += -= *= /=

# Arrays use {} for literals and {index} for access
arr = {1, 2, 3}
arr{0}           # → 1
arr << 4         # append

# Conditionals
if x > 0
    print "positive"
elif x == 0
    print "zero"
else
    print "negative"
end

# Loops
loop x > 0          # while loop
    x -= 1
end

loop i in 0..10      # for loop (exclusive upper bound)
    print i
end

loop i in 0..100 by 5   # for loop with step
    print i
end

loop item in arr     # foreach loop
    print item
end

break                # exit loop
next                 # skip to next iteration

# Functions — args in [], called with []
fun add [a, b]
    return a + b
end
result = add [3, 4]

# Default parameter values — trailing params can have = default_expr
# Desugared into null-checks at the top of the function body.
# Passing null (or omitting the arg) triggers the default.
fun greet [name, greeting = "Hello"]
    return greeting + ", " + name
end
greet ["World"]                  # → "Hello, World"
greet ["World", "Hi"]            # → "Hi, World"

# All params can be optional
fun point [x = 0, y = 0, z = 0]
    return x + y + z
end
point []        # → 0
point [1, 2]    # → 3

# Named arguments
fun greet [name, greeting]
    print "{greeting}, {name}!"
end
greet [greeting: "Hello", name: "World"]

# Classes — constructor args in [], property access with ::
class Point [x, y]
    fun magnitude []
        return Math_sqrt [this :: x * this :: x + this :: y * this :: y]
    end
end

p = new Point [3, 4]
print p :: x          # 3
p :: x = 10           # set property
p :: magnitude []     # call method

# Inheritance (including multiple)
class Animal [name]
    fun speak []
        return this :: name
    end
end
class Dog [name] : Animal [name]
end

# Nested classes: parent :: new NestedClass [args]

# Casting and type checking
obj as Animal          # cast (returns null on failure)
obj is Animal          # instanceof (returns bool)

# Exception handling
begin
    raise "something went wrong"
rescue err
    print err
ensure
    print "always runs"
end

# String interpolation
print "Hello {name}, you are {age} years old"

# Coroutines
fun my_coroutine []
    print "step 1"
    yield                   # yield until next tick
    print "step 2"
    yield wait 2.0          # yield for 2 seconds
    print "step 3"
end

# Import
import "other_script"

# Builtins
print value
assert condition

# Numeric separators
big = 1_000_000
```

## WarValue — The Core Type

Tagged union struct with 7 variants. Numeric and Logical are inline (no heap). Others use `Ref` field.

```csharp
// Creating values
WarValue.Null
WarValue.True / WarValue.False
WarValue.FromNumeric(42.0)
WarValue.FromText("hello")
WarValue.FromArray(new List<WarValue>())
WarValue.FromClass(classData)
WarValue.FromNativeObject(anyObject)

// Reading values
value.Tag          // ValueTag enum
value.IsNumeric    // predicate
value.NumericValue // double
value.TextValue    // string (cast from Ref)
value.ArrayValue   // List<WarValue> (cast from Ref)
value.ClassValue   // ClassData (cast from Ref)
```

## Bytecode VM Architecture

Stack-based VM with fixed-size arrays (no heap allocation in hot path):
- **Value stack**: 1024 WarValue slots
- **Call frames**: 128 CallFrame slots (Function, IP, StackBase)
- **Exception handlers**: 32 TryHandler slots

Key features:
- **Superinstructions**: Fused compare+jump (LessJump, EqualJump, etc.), This+GetProperty/SetProperty
- **Inline caching**: Property access caches ClassDetails + slot index per bytecode site
- **Tail call optimization**: TailCall opcode reuses current frame
- **Instruction budget**: Configurable limit per Run()/Call(), raises catchable exception
- **Memory budget**: Tracks string/array/class allocations, raises catchable exception
- **Source-map debugger**: Breakpoints, StepInto/StepOver/StepOut, locals inspection via DebugHook
- **Coroutine suspend/resume**: Yield opcodes pause VM state; BytecodeCoroutine wraps a dedicated WarVM

Operand encoding: opcodes are 1 byte, U16 operands are big-endian `[hi][lo]`.

## How To Add a New Native Function

### Option 1: Manual (like the stdlib)

Add to an existing library or create a new one:

```csharp
// In your library's Register method:
scope.AddFunction(new NativeFunctionDefinition(
    new FunctionDetails("my_func", new List<string> { "arg1", "arg2" }),
    args =>
    {
        var a = NativeHelper.NumericArg(args, 0);
        var b = NativeHelper.TextArg(args, 1);
        // ... do work ...
        return WarValue.FromNumeric(result);
    },
    "Description for docs.", "ReturnType"));
```

If creating a new library, register it in `WarScriptLibraryRegistry.Libraries[]`.

### Option 2: Attribute-based codegen

```csharp
[WsModule("my_module", Description = "My module")]
public static partial class MyModule
{
    [WsFunction("my_func", Doc = "Does a thing", Returns = "Numeric")]
    public static double MyFunc(double a, string b) => a + b.Length;
}
```

The `WsBindingGenerator` (Editor) auto-generates marshaling at build time. Supported param types: `double`, `int`, `float`, `string`, `bool`, `WarValue`, `List<WarValue>`.

## How To Add a New Language Feature

The typical change path for a new syntactic feature:

1. **Lexer** (`LexicalParser.cs`): Add new token recognition if needed (new keyword in `ClassifyWord`, new operator in `ScanToken`)
2. **Token** (`TokenType.cs`): Add new token type if needed (usually not — most features use existing Keyword/Operator types)
3. **Parser** (`StatementParser.cs` + `ExpressionReader.cs`): Add parsing logic that produces new AST nodes
4. **AST nodes** (`Statement/` or `Expression/`): Create new Statement or Expression classes
5. **Compiler** (`Compiler.cs`): Add visitor/emit logic for the new AST nodes → bytecode
6. **OpCode** (`OpCode.cs`): Add new opcodes if needed
7. **VM** (`WarVM.cs`): Add dispatch cases for new opcodes
8. **Tests**: Add .ws test scripts in `Tests/resources/` and NUnit tests

For expression-only features (new operator), the path is:
1. Lexer (recognize the token)
2. `OperatorExtension.cs` (map string → Operator enum, set precedence, wire up factory)
3. New operator class in `Expression/Operator/`
4. Compiler: handle the new expression type in `CompileExpression`
5. VM: handle new opcode if needed

## How To Add a New Opcode

1. Add the entry to `OpCode.cs` enum
2. Emit it in `Compiler.cs` (in the appropriate Compile* method)
3. Handle it in `WarVM.cs` Execute() switch (the main dispatch loop)
4. Update `Chunk.DisassembleInstruction()` for debug printing
5. Update `BytecodeSerializer` if serialization is needed

## Scope System

Two parallel scope stacks:
- **DefinitionScope** (via `DefinitionContext`): Holds function and class *definitions*. Searched at compile time.
- **MemoryScope** (via `MemoryContext`): Holds variable *values* at runtime. Dict-based with parent chain for lookup, `Set()` walks up the chain, `SetLocal()` writes to current scope only.

The VM uses stack-based locals for function bodies (slots 0..N) and falls back to MemoryScope for globals (`GetGlobal`/`SetGlobal` opcodes).

MemoryContext has object pooling: scopes marked `Poolable = true` are recycled.

## Testing Conventions

- **Framework**: NUnit (`[TestFixture]`, `[Test]`, `Assert.AreEqual`)
- **Helper**: `TestHelper.Run("name", source)` returns `(script, List<string> output)` — output captures all `print` statements
- **Helper**: `TestHelper.RunFile("filename.ws")` loads from `Tests/resources/`
- **Pattern**: Most tests either check print output via `Assert.AreEqual(expected, output)` or rely on `assert` statements in .ws scripts (which throw on failure)
- **Setup**: Optional `setupScope` callback for registering native functions before execution

```csharp
[Test]
public void MyFeature()
{
    var (_, output) = TestHelper.Run("test", @"
        x = 42
        print x + 1
    ");
    Assert.AreEqual(new[] { "43" }, output);
}
```

## Key Invariants & Gotchas

- **AST is discarded after compilation.** Don't hold references to Statement/Expression nodes after `Run()`.
- **`WarValue` is a struct.** Passed by value. Use `in` keyword for read-only passing in hot paths.
- **Constant pool deduplication**: `Chunk.AddConstant` deduplicates by tag+value. Indices are U16 (max 65535 constants per function).
- **`{` and `}` are array delimiters**, not block delimiters. Blocks end with `end`. Array indexing is `arr{i}`, not `arr[i]`. Function args use `[`, `]`.
- **`::` is the property access operator**, not `.` — e.g., `obj :: name`, `this :: x`.
- **Operator precedence** (highest to lowest): Unary/Class (7) → Multiplicative (6) → Additive (5) → Comparison (4) → Parens (3) → And (2) → Or (1) → Assignment/Append (0).
- **Expression parsing uses shunting-yard algorithm** (`ExpressionReader`), not recursive descent.
- **HaltFlags are a bitmask.** Multiple halt conditions can be set simultaneously (e.g., exception during yield). Always clear with `HaltFlags = HaltFlag.None` after handling.
- **Tree-walk execution still exists** as a fallback path (for functions called before `Run()`, and the legacy `Coroutine` class). The bytecode path is preferred and tested more heavily.
- **Lexer caches globally** by source string. Call `LexicalParser.ClearCache()` if source changes (hot reload).
- **String interpolation** `"{expr}"` is desugared by the lexer into concatenation tokens. It never reaches the parser as a special node.
- **Default parameters** are desugared by the parser into `if param == null` → `param = default` at the top of the function body. Passing `null` explicitly triggers the default. Functions are registered at all valid arities (`MinArity..ArgCount`). The VM pads missing stack slots with null.
- **Import** resolves via the `FileResolver` delegate passed at construction. Returns null if file not found. Import results are cached in `ImportCache`.
- **Coroutines**: Each `BytecodeCoroutine` owns its own `WarVM` instance. This is intentional — the VM's full state (stack, frames, handlers) is preserved across yields.
- **Function lookup is by (name, argCount).** Overloading by arity is supported. Default params register at multiple arities so `f[a]` finds `fun f [a, b = 1]`.

## Existing Desugar Patterns

Features implemented as desugars (no dedicated opcodes):

1. **String interpolation** — Lexer desugars `"hello {x}"` into `"hello " + (x)` at the token level.
2. **Default parameters** — Parser desugars `fun f [a, b = 1]` by injecting `if b == null then b = 1` ConditionStatements at the top of the function body. `DefinitionScope` registers at all valid arities. VM pads stack with nulls when `argCount < Arity`.
3. **Compound assignment** — `x += 1` is parsed as `x = x + 1` by the expression reader.

## Performance-Sensitive Areas

- `WarVM.Execute()` — the main dispatch loop. Every instruction goes through here. Avoid allocations, virtual dispatch, and unnecessary branching in opcode handlers.
- `MemoryScope.Get()` / `.Set()` — called on every global variable access. Dict lookup + parent chain walk.
- `InlineCache` on property access — avoids string hashing on repeated access to the same property on the same class type.
- `StringInterner` — reduces GC pressure for short strings. Pre-allocated integer strings 0–999.
