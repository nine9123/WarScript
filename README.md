<p align="center">
  <img src="warscript-logo-wordmark.png" alt="WarScript" width="520">
</p>

<p align="center">
  A lightweight, embeddable, deterministic scripting language with a bytecode VM written in C#.
</p>

---

WarScript is a custom scripting language designed to be embedded into C# applications and Unity
projects. It provides a simple, Ruby-flavoured syntax for scripting game logic, automation, and
runtime behaviour without recompiling your project.

Every WarScript number is a **32.32 fixed-point value** (`FixMath.F64`) rather than a float, so the
same script produces bit-identical results on every machine and platform — it is built to be the
scripting layer of a lockstep-networked game.

## Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Language Guide](#language-guide)
  - [Comments](#comments) · [Variables](#variables) · [Types](#types) · [Numbers](#numbers-and-fixed-point)
  - [Operators](#operators) · [Strings](#strings) · [Arrays](#arrays)
  - [Conditionals](#conditionals) · [Loops](#loops)
  - [Functions](#functions) · [Lambdas](#lambdas--first-class-functions)
  - [Constants](#constants) · [Enums](#enums) · [Classes](#classes)
  - [Exceptions](#exception-handling) · [Coroutines](#coroutines-and-yield) · [Import](#import)
- [Standard Library](#standard-library)
- [Host Integration (C#)](#host-integration-c)
- [Coroutines: making `yield` work](#coroutines-making-yield-work) ← **read this if `yield` seems broken**
- [Gotchas and Limitations](#gotchas-and-limitations)

## Installation

### Unity (UPM)

Open **Window → Package Manager → + → Add package from git URL** and enter:

```
https://github.com/nine9123/WarScript.git#upm
```

**WarScript requires FixPointCS and does not ship it.** `WarScript.Runtime.asmdef` references an
assembly named `FixPointCS` and sets `noEngineReferences: true`; the consuming project must provide
it (it is deliberately *not* listed in `package.json` `dependencies`, so UPM will not fetch it for
you). Add [FixPointCS](https://github.com/XMunkki/FixPointCS) — the `Fixed64`/`FixedUtil` sources
plus the `FixMath` folder — to your project inside an assembly definition named `FixPointCS`. If you
see `The type or namespace name 'FixMath' could not be found`, this is the missing piece.

### C# Projects

Clone or add as a submodule, compile `Runtime/` and `Attributes/` into your project (or reference
them as a class library), and compile FixPointCS alongside:

```bash
git clone https://github.com/nine9123/WarScript.git
```

`Runtime/` has no Unity dependency — it is plain C# and runs in a console app or on a server.
`Editor/` is Unity-only (it hosts the binding generator).

## Quick Start

```csharp
using FixMath;
using WarScript;
using WarScript.Context.Definition;
using WarScript.Expression.Value;

// 1. Create the script. The logger receives everything `print` emits and any
//    uncaught error's stack trace.
var script = new WarScriptLanguage(
    scriptName:   "example",
    sourceCode:   sourceCode,
    fileResolver: null,                       // resolves `import "name"` — see Import
    logger:       (s, msg) => Debug.Log(msg));

// 2. Register the built-in libraries (and your own bindings) BEFORE Run().
WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);

// 3. Run() parses, compiles to bytecode, and executes all top-level statements.
//    This is also what makes functions callable — always call it first.
script.Run();

// 4. Call into the script. Functions are looked up by (name, argument count).
var tick = script.GetFunction("tick", 1);
script.Call(tick, WarValue.FromNumeric(F64.Ratio(1, 60)));

// 5. If your scripts use `yield`, drive coroutines once per tick.
script.TickCoroutines(F64.Ratio(1, 60));
```

There is **no separate compile step to opt into** — `Run()` always compiles the script to bytecode,
and every language feature (including `yield`) runs on the bytecode VM. Saving/loading bytecode
files is an optional build-time optimisation, not a requirement. See
[Bytecode serialization](#bytecode-serialization).

## Language Guide

Every example below has been executed against the runtime; the `# →` comments show real output.

### Comments

```ruby
# a full-line comment
x = 1   # a trailing comment
```

### Variables

Dynamically typed, no declaration keyword — just assign. An unassigned name reads as `null` rather
than raising an error.

```ruby
x = 42
name = "hello"
active = true
data = null

print x         # → 42
print name      # → hello
print active    # → True
print data      # → null
print never_set # → null
```

### Types

WarScript has 7 value types:

| Type | Example | Notes |
|---|---|---|
| **Numeric** | `42`, `0.5`, `-3` | `F64` 32.32 fixed-point — *not* a float or double |
| **Logical** | `true`, `false` | prints as `True` / `False` |
| **Text** | `"hello"` | interpolation, escapes, and raw `"""..."""` |
| **Array** | `{1, 2, 3}` | prints as `[1, 2, 3]` |
| **Class** | `new Point [1, 2]` | prints as the class name |
| **Null** | `null` | |
| **NativeObject** | supplied by the host | opaque handle to a C# object (e.g. `F64Vec3`) |

Anything that is not `false` or `null` is truthy, so `if 1` takes the branch.

### Numbers and fixed point

Numeric literals are `'-'? digit+ ('.' digit+)?`. Fractional literals are parsed to exact
fixed-point **using integer arithmetic only**, so the bits are identical on every platform.

```ruby
print 0.5           # → 0.5
print 99.5          # → 99.5
print 1.25 + 0.25   # → 1.5
print 10 / 4        # → 2.5
print 1_000_000     # → 1000000     (numeric separators)
print 3.141_592     # → 3.141592
```

Values that are exactly representable in 32.32 round-trip exactly. Everything else is
deterministically truncated, and results of division and transcendental functions show their
fixed-point nature:

```ruby
print 1 / 3      # → 0.3333333332557231
print sqrt [2]   # → 1.4142135614529252
print pow [2, 10]# → 1023.999994755   (not exactly 1024)
```

- Integer part range is ±2147483647; fractional precision past 9 digits is truncated.
- `round` is **half-up**, not banker's rounding.
- Malformed literals raise a `SyntaxException` — there is no silent coercion.
- Division and modulo by zero raise a *catchable* script error (see
  [Exception handling](#exception-handling)), they do not crash the host.

### Operators

```ruby
# Arithmetic
print 10 + 3    # → 13
print 10 - 3    # → 7
print 10 * 3    # → 30
print 10 / 4    # → 2.5
print 10 % 3    # → 1

# Comparison
print 1 == 1    # → True
print 1 != 2    # → True
print 1 < 2     # → True
print 2 <= 2    # → True
print 3 > 2     # → True
print 3 >= 4    # → False

# Logical
print true and false   # → False
print true or false    # → True
print !true            # → False

# Compound assignment
x = 10
x += 5    # 15
x -= 3    # 12
x *= 2    # 24
x /= 4    # 6
```

Comparison coerces to text when the operands differ, so `1 == "1"` is `True` and `"a" < "b"` is
`True`. `null == null` is `True`, but `null == 0` is `False`.

### Strings

```ruby
print "hello" + " world"        # → hello world   (concatenate)
print "hello world" - "world"   # → "hello "      (remove first occurrence)
print "ab" * 3                  # → ababab        (repeat)
print "n=" + 5                  # → n=5           (numbers coerce to text)
```

#### Interpolation

`{...}` inside a text literal is replaced by the expression's value.

```ruby
name = "hero"
age = 7
print "Hello {name}, you are {age} years old"   # → Hello hero, you are 7 years old
print "sum: {1 + 2}"                            # → sum: 3
```

`$"..."` is the same literal written explicitly, for when the intent matters:

```ruby
print $"Hello {name}"                           # → Hello hero
```

Braces nest, so `"{arr{0}}"` indexes inside an interpolation, and a text literal *inside* an
interpolation keeps its own braces and quotes: `"result: {"a}b"}"` is `result: a}b`.

#### Escapes

A backslash escapes the character after it. The sequences are `\"`, `\\`, `\{`, `\}`, `\n`, `\t`
and `\r`; anything else is a syntax error, so a stray backslash is caught rather than silently
kept.

```ruby
print "she said \"hi\""     # → she said "hi"
print "\{name\} is literal" # → {name} is literal
print "a\nb"                # → two lines
```

#### Raw literals

`"""..."""` interprets nothing — no escapes, no interpolation — and may span lines. This is how
you carry WarScript source, JSON, or anything else brace- and quote-heavy as a value.

```ruby
snippet = """print "{greeting}, {party{0}}""""

dialog = """
if reputation > 50
    npc_say ["Good to see you again."]
end
"""
```

A line break immediately after the opening delimiter and one immediately before the closing
delimiter are dropped, so the block above is exactly its three lines with no leading or trailing
blank line. A run of four or more quotes closes with its last three, which is why the `snippet`
line above ends in a quote character; content containing a run of three quotes cannot be written
raw. `$"""..."""` is a syntax error — raw literals do not interpolate; concatenate instead.

### Arrays

Arrays use `{}` for literals and `{index}` for access (zero-based).

```ruby
arr = {1, 2, 3}
print arr{0}              # → 1
arr << 4                  # append
print arr                 # → [1, 2, 3, 4]
arr{1} = 99               # assign by index
print arr                 # → [1, 99, 3, 4]
print Array_length [arr]  # → 4

mixed = {1, "a", true, null}
print mixed               # → [1, a, True, null]
```

Reading an out-of-range or negative index yields `null` — it is not an error.

**Nested arrays cannot be indexed in one expression.** `grid{0}{1}` silently parses as something
else; assign to a temporary first:

```ruby
grid = {{1, 2}, {3, 4}}
row = grid{0}
print row{1}     # → 2

print grid{0}{1} # → [1]   ← WRONG, do not do this
```

See [`Array_*` helpers](#standard-library) for search, insert, remove, and copy.

### Conditionals

```ruby
health = 30
if health > 50
    print "healthy"
elif health > 0
    print "wounded"    # → wounded
else
    print "dead"
end
```

### Loops

All four loop forms use the `loop` keyword; the shape of the header decides which one you get.

```ruby
# While
health = 30
loop health > 0
    health -= 10
end

# For over a range (upper bound exclusive)
loop i in 0..3
    print i          # → 0, 1, 2
end

# For with a step
loop i in 0..10 by 5
    print i          # → 0, 5
end

# Foreach over an array
loop item in {"a", "b"}
    print item       # → a, b
end
```

`break` exits the loop, `next` skips to the following iteration:

```ruby
loop i in 0..10
    if i == 1
        next
    end
    if i == 3
        break
    end
    print i          # → 0, 2
end
```

The loop variable is local to the loop and does not clobber a global of the same name.

### Functions

Parameters are declared in `[]` and functions are called with `[]`.

```ruby
fun add [a, b]
    return a + b
end
print add [3, 4]     # → 7

fun fib [n]
    if n < 2
        return n
    end
    return fib [n - 1] + fib [n - 2]
end
print fib [10]       # → 55
```

Functions are resolved by **(name, argument count)** — two functions with the same name and
different arities are distinct.

#### Default parameters

Trailing parameters may have defaults. The function is registered at every valid arity.

```ruby
fun greet [name, greeting = "Hello"]
    return greeting + ", " + name
end
print greet ["World"]           # → Hello, World
print greet ["World", "Hi"]     # → Hi, World
```

Passing `null` explicitly also triggers the default — there is no way to distinguish "missing" from
"null".

#### Named arguments

```ruby
fun create_unit [name, hp, team]
    print "{name} ({hp} HP) on team {team}"
end
create_unit [team: "Red", name: "Soldier", hp: 100]   # → Soldier (100 HP) on team Red
```

#### Multi-line calls

Argument lists may span lines:

```ruby
print add [
    1,
    2
]                    # → 3
```

### Lambdas / first-class functions

Functions are values. `fun [params] ... end` is valid in any expression position.

```ruby
double = fun [x] return x * 2 end
print double [5]     # → 10

# Pass one in
fun apply [arr, f]
    result = {}
    loop item in arr
        result << f [item]
    end
    return result
end
print apply [{1, 2, 3}, fun [x] return x * 10 end]   # → [10, 20, 30]

# Return one out
fun pick_strategy [mode]
    if mode == "aggressive"
        return fun [u] return u :: attack [] end
    end
    return fun [u] return u :: defend [] end
end

# Store them in arrays
ops = {fun [a, b] return a + b end, fun [a, b] return a * b end}
f = ops{0}
print f [3, 4]       # → 7
```

**Lambdas do not close over enclosing locals.** They see their own parameters and globals only:

```ruby
factor = 3                              # global
f = fun [x] return x * factor end
print f [5]                             # → 15   ✔ globals work

fun outer []
    n = 10                              # local to `outer`
    g = fun [x] return x + n end
    return g [1]
end
print outer []                          # → 1null   ✘ `n` is null inside the lambda
```

### Constants

Immutable globals. Reassignment is rejected at **parse time** with a `SyntaxException`, so it fails
before anything runs.

```ruby
const MAX_HP = 100
const TEAM = "Red"
const HALF = MAX_HP / 2   # the expression is evaluated at runtime

print MAX_HP   # → 100
print HALF     # → 50

# MAX_HP = 1   ← SyntaxException: Cannot reassign constant 'MAX_HP'
```

### Enums

Named numeric constants grouped under a class instance, accessed with `::`. The enum name itself is
protected from reassignment.

```ruby
enum DamageType
    PHYSICAL
    MAGICAL
    TRUE = 5
end

print DamageType :: PHYSICAL   # → 0
print DamageType :: MAGICAL    # → 1
print DamageType :: TRUE       # → 5
print DamageType :: name [0]   # → PHYSICAL
print DamageType :: values     # → [0, 1, 5]
print DamageType :: names      # → [PHYSICAL, MAGICAL, TRUE]
print DamageType :: count      # → 3

loop v in DamageType :: values
    print DamageType :: name [v]
end
```

Members auto-increment from the previous value; an explicit value resets the counter:

```ruby
enum Priority
    LOW = 10        # 10
    MEDIUM          # 11
    HIGH            # 12
    CRITICAL = 100  # 100
    EMERGENCY       # 101
end
```

### Classes

Constructor parameters are declared in `[]`; properties and methods are reached with `::`.

```ruby
class Point [x, y]
    fun magnitude []
        return sqrt [this :: x * this :: x + this :: y * this :: y]
    end
end

p = new Point [3, 4]
print p :: x               # → 3
print p :: magnitude []    # → 5.000000085681677   (fixed-point sqrt)

p :: x = 10                # properties are assignable
print p :: x               # → 10
```

#### Inheritance

```ruby
class Animal [name]
    fun speak []
        return this :: name + " makes a sound"
    end
end

class Dog [name] : Animal [name]
    fun speak []
        return this :: name + " says woof"
    end
end

d = new Dog ["Rex"]
print d :: speak []   # → Rex says woof
print d :: name       # → Rex
```

Multiple inheritance is supported — list each base with the arguments to forward:

```ruby
class User [email]
    fun get_email [] return this :: email end
end
class Person [name]
    fun get_name [] return this :: name end
end
class Student [email, name] : User [email], Person [name]
end

s = new Student ["a@b.c", "Ann"]
print s :: get_email []   # → a@b.c
print s :: get_name []    # → Ann
```

#### Type tests and casts

```ruby
print d is Dog       # → True
print d is Animal    # → True
a = d as Animal      # cast, or null if incompatible
print a :: name      # → Rex
```

### Exception handling

`raise` throws, `rescue` catches (binding the value to a variable), `ensure` always runs.

```ruby
begin
    raise "something went wrong"
rescue err
    print "Caught: " + err     # → Caught: something went wrong
ensure
    print "always runs"        # → always runs
end
```

Runtime errors are catchable the same way:

```ruby
begin
    x = 1 / 0
rescue err
    print "Caught: " + err     # → Caught: Division by zero
end

begin
    y = 1 % 0
rescue err
    print "Caught: " + err     # → Caught: Modulo by zero
end
```

`raise` can carry a class instance, not just text, so you can rescue structured errors.

An uncaught error stops the script and sends its message to the host `logger` — the host process
keeps running.

#### assert

```ruby
assert 1 == 1        # passes silently

begin
    assert 1 == 2
rescue err
    print err        # → Assertion error at line <line of the assert>
end
```

An uncaught failing `assert` aborts the rest of the script and logs `Assertion error at line N`.

### Coroutines and `yield`

A coroutine is an ordinary function that the **host** starts and then resumes once per tick. `yield`
suspends it; the host's next `TickCoroutines(dt)` call continues from exactly that point, with all
local variables intact.

```ruby
fun patrol []
    print "moving to point A"
    yield wait 2.0            # resume ~2 seconds later
    print "moving to point B"
    yield                     # resume on the next tick
    loop i in 0..3
        print "scanning {i}"
        yield                 # yield inside loops is fine
    end
    yield until enemy_spotted  # resume once the condition is true
    print "engaging"
end
```

| Form | Resumes |
|---|---|
| `yield` | on the next `TickCoroutines` call |
| `yield wait N` | after `N` seconds of accumulated `dt` |
| `yield until <condition>` | on the first tick where the condition is truthy (re-evaluated each tick) |

`yield` works anywhere — inside loops, inside `if` blocks, inside `begin`/`rescue`, and inside
nested function and lambda calls, because the whole VM state is suspended:

```ruby
fun do_step [n]
    print "step {n}"
    yield
    print "step {n} done"
end

fun main_routine []
    do_step [1]
    do_step [2]
end
```

`begin`/`rescue` survives a suspension, so this catches normally:

```ruby
fun risky []
    begin
        yield
        raise "boom"
    rescue err
        print "caught: " + err
    end
end
```

Coroutines read and write the script's globals, so a coroutine and the host see the same state.

#### Starting coroutines from script

```ruby
fun patrol [] print "go" yield print "stop" end

id = start_coroutine ["patrol", {}]         # the args array is REQUIRED, even when empty
loop_id = start_coroutine_loop ["patrol", {}]   # restarts automatically when it finishes
stop_coroutine [id]
stop_all_coroutines []
```

> `start_coroutine ["patrol"]` — with only one argument — fails with
> `Function 'start_coroutine' with 1 args is not defined`, because native functions are matched on
> exact arity. Always pass the second argument: `start_coroutine ["patrol", {}]`.

Arguments are passed as an array and bound to the coroutine function's parameters:

```ruby
fun greet [name] print "hi {name}" yield print "bye {name}" end
start_coroutine ["greet", {"hero"}]
```

**A coroutine started from script (or from C#) does nothing further until the host calls
`TickCoroutines(dt)`.** See [Coroutines: making `yield` work](#coroutines-making-yield-work).

### Import

```ruby
import "utils"
import "ai/patrol"
```

The path is resolved by the `fileResolver` delegate you pass to the constructor; WarScript never
touches the filesystem itself. Return the imported script's source, or `null` if it cannot be found.

```csharp
var script = new WarScriptLanguage("main", source,
    fileResolver: path => File.Exists($"Scripts/{path}.ws")
        ? File.ReadAllText($"Scripts/{path}.ws")
        : null,
    logger: (s, msg) => Debug.Log(msg));
```

Imports are cached per script instance, and import cycles are detected.

## Standard Library

Registered by `WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope)`. Note the
naming: **Math and Utility functions are unprefixed, Array functions carry an `Array_` prefix.**

### Math

Fixed-point implementations from FixPointCS — deterministic across platforms, but approximate
compared with real arithmetic (`pow [2, 10]` is `1023.999994755`).

| Function | Description |
|---|---|
| `pow [base, exp]` | `base` raised to `exp` |
| `sqrt [n]` | square root |
| `floor [n]` / `ceil [n]` / `round [n]` | rounding (`round` is half-up) |
| `abs [n]` | absolute value |
| `min [a, b]` / `max [a, b]` | smaller / larger |
| `clamp [n, lo, hi]` | clamp `n` to the range |
| `sign [n]` | `-1`, `0`, or `1` |
| `lerp [a, b, t]` | linear interpolation |
| `sin [radians]` / `cos [radians]` / `tan [radians]` | trigonometry |
| `asin [n]` / `acos [n]` / `atan2 [y, x]` | inverse trigonometry |
| `deg_to_rad [degrees]` / `rad_to_deg [radians]` | angle conversion |
| `pi []` | π — note the empty argument list |

```ruby
print floor [1.7]        # → 1
print clamp [10, 0, 5]   # → 5
print lerp [0, 10, 0.5]  # → 5
print pi []              # → 3.141592653701082
```

There are deliberately **no random functions** — `System.Random` is a lockstep desync source.
Deterministic randomness is supplied by the host as its own module.

### Array

| Function | Description |
|---|---|
| `Array_length [arr]` | element count |
| `Array_contains [arr, value]` | `true` if present |
| `Array_index_of [arr, value]` | index of first occurrence, or `-1` |
| `Array_insert [arr, index, value]` | insert at index |
| `Array_remove_at [arr, index]` | remove at index, return the removed element |
| `Array_remove [arr, value]` | remove first occurrence, return `true` if found |
| `Array_pop [arr]` | remove and return the last element |
| `Array_clear [arr]` | remove all elements |
| `Array_copy [arr]` | shallow copy |

```ruby
arr = {10, 20, 30, 40}
print Array_contains [arr, 20]   # → True
print Array_index_of [arr, 30]   # → 2
print Array_pop [arr]            # → 40
print arr                        # → [10, 20, 30]
```

### Coroutine

| Function | Description |
|---|---|
| `start_coroutine [name, args]` | start a coroutine, returns its id |
| `start_coroutine_loop [name, args]` | start a coroutine that restarts when it completes |
| `stop_coroutine [id]` | stop one coroutine |
| `stop_all_coroutines []` | stop every coroutine |

### Utility

| Function | Description |
|---|---|
| `is_null [object]` | `true` if the value is null |

### Built-in statements

`print <expr>` writes to the host logger. `assert <condition>` raises on failure.

## Host Integration (C#)

### Lifecycle

```csharp
var script = new WarScriptLanguage(name, source, fileResolver, logger);
WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);  // before Run()
RegisterMyOwnBindings(script, script.GlobalDefinitionScope);                 // before Run()
script.Run();                            // parse + compile + execute top-level code
var fn = script.GetFunction("tick", 1);  // lookup is by (name, argument count)
script.Call(fn, WarValue.FromNumeric(dt));
```

`Run()` compiles on first use and caches; calling it again re-executes the top-level statements. The
AST is discarded after compilation — the bytecode is the source of truth.

### Passing values in and out

`Call()` returns `void`. To get a result back, have the script write a global and read it from the
host:

```csharp
// script:  result = 0
//          fun compute [a, b] result = a + b return result end
script.Call(script.GetFunction("compute", 2)!,
            WarValue.FromNumeric(F64.FromInt(3)),
            WarValue.FromNumeric(F64.FromInt(4)));

WarValue result = script.UserMemoryScope.Get("result");   // → 7
int asInt = F64.RoundToInt(result.Numeric);
```

Globals can also be injected before a call, which is the usual way to hand per-frame state to a
script:

```csharp
script.UserMemoryScope.Set("hp", WarValue.FromNumeric(F64.FromInt(77)));
script.Call(script.GetFunction("show", 0)!);   // script can now read `hp`
```

Constructing and reading values:

```csharp
WarValue.FromNumeric(F64.FromInt(5));   // Numeric — F64 only, never float/double
WarValue.FromText("hello");             // Text
WarValue.FromLogical(true);             // Logical
WarValue.FromArray(listOfWarValues);    // Array
WarValue.FromNativeObject(myVec3);      // NativeObject (opaque handle)
WarValue.Null;

if (v.IsNumeric) { F64 n = v.Numeric; }
if (v.IsText)    { string s = v.TextValue; }
if (v.IsArray)   { var list = v.ArrayValue; }
```

`F64` has **no** conversion operator to `int`/`float`/`double`: `(int)someF64` does not compile. Use
`F64.RoundToInt(x)` to round or `F64.FromInt(n)` to build. Comparisons against `int` do work, so
`someF64 == 3` is fine.

### Driving a script from Unity

Use a **fixed** timestep — `Time.deltaTime` is a float and varies per machine, which defeats
determinism.

```csharp
public class WarScriptHost : MonoBehaviour
{
    private static readonly F64 Dt = F64.Ratio(1, 60);   // exact 1/60, no float involved

    private WarScriptLanguage _script;
    private FunctionDefinition _tick;

    void Awake()
    {
        _script = new WarScriptLanguage("ai", _source, ResolveImport, (s, m) => Debug.Log(m));
        WarScriptLibraryRegistry.RegisterAll(_script, _script.GlobalDefinitionScope);
        _script.InstructionBudget = 100_000;   // guard against runaway scripts
        _script.Run();
        _tick = _script.GetFunction("tick", 1);
    }

    void FixedUpdate()
    {
        if (_tick != null)
            _script.Call(_tick, WarValue.FromNumeric(Dt));

        _script.TickCoroutines(Dt);   // REQUIRED — without this, `yield` never resumes
    }
}
```

### Coroutine API

```csharp
int id    = script.StartCoroutine("patrol", new[] { WarValue.FromText("A") });
int id2   = script.StartCoroutine("pulse", Array.Empty<WarValue>(), loop: true);
bool ok   = script.StopCoroutine(id);
script.StopAllCoroutines();
int alive = script.TickCoroutines(dt);        // resume all ready coroutines; returns the live count
int count = script.ActiveCoroutineCount;
```

`StartCoroutine` runs the function immediately up to its first `yield`, then returns. It returns
`-1` if the function could not be found. Each call creates an independent coroutine with its own
state, so the same function can run several times concurrently.

### Adding native functions

#### Manual

```csharp
scope.AddFunction(new NativeFunctionDefinition(
    new FunctionDetails("my_func", new List<string> { "a", "b" }),
    args => WarValue.FromNumeric(
        NativeHelper.NumericArg(args, 0) + NativeHelper.TextArg(args, 1).Length),
    "Description shown in generated docs", "NumericValue"));
```

`NativeHelper` provides `NumericArg` (`F64`), `IntArg` (truncating `int`), `TextArg`, `ArrayArg`
(returns the array `WarValue`), and `NativeArg<T>` for native objects; read booleans directly with
`args[i].LogicalValue`. Remember that native functions match on **exact** argument count — register
an overload per arity if you want optional arguments.

#### Attribute-based codegen (recommended)

```csharp
[WsModule("combat", Description = "Combat system")]
public static partial class CombatModule
{
    [WsEnum] public enum DamageType { Physical, Magical, True = 5 }

    [WsConst] public const int   MAX_HP          = 100;
    [WsConst] public const float CRIT_MULTIPLIER = 2.5f;   // baked to a fixed-point raw

    [WsFunction("deal_damage", Doc = "Applies damage.", Returns = "NumericValue")]
    public static F64 DealDamage(F64 amount, F64 type) => amount;

    // Omit the name to get snake_case from the method name: ApplyBuff → apply_buff
    [WsFunction] public static void ApplyBuff(int unitId, string buff) { }

    // Variadic: take the raw argument list
    [WsFunction("print_all")]
    public static void PrintAll([WsRawArgs] List<WarValue> args) { }
}
```

Run **WarScript → Generate Bindings** in Unity. The generator emits a `Register(script, scope)` that
wires up functions (with marshaling), enums (as `::`-accessible class instances with `name[]`,
`values`, `names`, `count`), and consts (as immutable globals). Function names are **not** prefixed
with the module name.

Instance modules are supported too — mark a field with `[WsScript]` to receive the
`WarScriptLanguage` instance.

**Marshalable types:** `F64`, `int`, `bool`, `string`, `WarValue`, `List<WarValue>`, `F64Vec3`.
`double` and `float` parameters or return types are **rejected at generation time** — a single one
aborts the whole run. Use `F64`. Because an `F64` parameter cannot carry a C# compile-time default,
model an optional numeric as `WarValue x = default` and read
`x.IsNumeric ? x.Numeric : fallback` (an omitted argument arrives as `Null`).

### Safety budgets

```csharp
script.InstructionBudget = 100_000;     // max bytecode instructions per Run()/Call()/coroutine resume
script.MemoryBudget      = 1_000_000;   // max tracked heap bytes per Run()/Call()/coroutine resume
```

`0` (the default) means unlimited. Both are enforced per host invocation, so a coroutine gets a
fresh budget on every resume.

`Instruction budget exceeded` is raised as a normal script error and is reliably catchable:

```ruby
begin
    loop i in 0..1000000
        x = i
    end
rescue err
    print "caught: " + err     # → caught: Instruction budget exceeded
end
print "still running"
```

`Memory budget exceeded` is raised at allocation sites (strings, arrays, class instances) but is
**only catchable at some of them** — for example `s = s + s` can be rescued, while
`arr << "data"` currently escapes `begin`/`rescue` and stops the script with an uncaught error.
Treat the memory budget as a hard backstop rather than a recoverable condition.

### Debugger

Set `DebugHook` to be called on breakpoints and step points; leave it `null` for zero overhead.

```csharp
script.AddBreakpoint(42);
script.DebugHook = ctx =>
{
    Debug.Log($"Paused at {ctx.FunctionName}:{ctx.Line}");
    foreach (var local in ctx.Locals)
        Debug.Log($"  {local.Key} = {local.Value}");
    ctx.Action = StepMode.StepOver;   // StepInto / StepOut / Continue
};
```

`RemoveBreakpoint(line)`, `ClearBreakpoints()`, and `Breakpoints` round out the API. `DebugHook`,
`DebugContext`, and `StepMode` live in the `WarScript.Bytecode` namespace; `ctx.CallStack` gives the
current frames as `FunctionName:Line` entries.

### Hot reload

```csharp
LexicalParser.ClearCache();       // the lexer caches globally by source string
script.Reload(newSource);
tickFunc = script.GetFunction("tick", 1);   // old handles are stale — re-acquire
```

Global variables survive; function and class definitions are replaced; **all coroutines are
stopped** (they hold bytecode that no longer exists), and top-level statements are *not* re-executed.

```
counter = 5; bump[] → 6         # before reload
# reload with `counter += 100`
bump[] → 106                    # global preserved, new body
```

### Bytecode serialization

Optional: precompile at build time to skip the lexer, parser, and compiler at load.

```csharp
// Build step
var script = new WarScriptLanguage("patrol", source, null, null);
script.Run();
using var fs = File.Create("patrol.wsbc");
script.SaveBytecode(fs);

// Runtime
using var fs = File.OpenRead("patrol.wsbc");
script.LoadBytecode(fs);              // replaces definitions, preserves globals, stops coroutines
var tick = script.GetFunction("tick", 1);
```

Coroutines and `yield` behave identically whether the bytecode came from `Run()` or `LoadBytecode()`.

> The serialized format version is `1`, but numeric constants are stored as the 64-bit **F64 raw**.
> Files produced before the fixed-point migration share the version byte yet are binary
> incompatible — regenerate them from source.

## Coroutines: making `yield` work

**`yield` does not require any opt-in compilation step.** `Run()` always compiles to bytecode, and
all coroutines execute on the bytecode VM, which is what makes `yield` work inside loops, `if`
blocks, and nested calls. If `yield` appears not to work, it is the integration, and it is almost
always one of the following.

A coroutine only makes progress when **the host resumes it**. There are exactly two required
ingredients:

1. Start it with `StartCoroutine(...)` (C#) or `start_coroutine ["name", {}]` (script).
2. Call `script.TickCoroutines(dt)` once per tick, forever.

### Troubleshooting table

| Symptom | Cause | Fix |
|---|---|---|
| Code up to the first `yield` runs once, and calling the function again restarts it from the top | The function was invoked with `script.Call(fn)` instead of being started as a coroutine. `Call()` executes until the first `yield`, then returns — the suspended state is discarded. | `script.StartCoroutine("fn", args)` |
| Code up to the first `yield` runs, then nothing ever happens again | The host never calls `TickCoroutines(dt)`. | Call `script.TickCoroutines(dt)` every tick |
| `yield` advances but `yield wait N` never completes | `dt` is zero (paused game), or is not in seconds. | Pass real elapsed seconds as `F64` — e.g. `F64.Ratio(1, 60)` |
| `StartCoroutine` returns `-1` and nothing runs | The function name/arity does not resolve: called **before `Run()`**, wrong argument count, or the name is a **class method** (only top-level functions can be coroutines). | Call `Run()` first; match the arity; move the body to a top-level function |
| Nothing in the script runs at all, and the log shows `Coroutine function 'x' ... is not defined` | `StartCoroutine` was called before `Run()`. The failure leaves a raised error pending, which then aborts the following `Run()` before its first statement. | Always `Run()` before starting coroutines |
| Log shows `Function 'start_coroutine' with 1 args is not defined` | Native functions match on exact arity. | `start_coroutine ["name", {}]` — pass the args array even when empty |
| A `loop`+`yield` coroutine only advances one iteration per tick | Correct by design: one resume per coroutine per `TickCoroutines` call, regardless of how large `dt` is. | Tick more often, or don't `yield` in the inner loop |
| A looping coroutine seems to skip a tick between repetitions | Also by design: `loop: true` restarts the function on the *next* tick after it completes. | — |
| The coroutine vanished silently | An uncaught error inside a coroutine logs its message and drops that coroutine (`ActiveCoroutineCount` decreases). Or the code path ran `Reload()` / `LoadBytecode()`, both of which stop all coroutines. | Check the logger output; restart coroutines after a reload |
| `script.IsYielded` is stuck `true` | `Call()`ing a function that hits a `yield` sets this script-level flag and nothing clears it. It is not a reliable signal. | Use `ActiveCoroutineCount`; call `script.ClearYield()` if you need the flag reset |

### A correct, minimal end-to-end example

```ruby
# patrol.ws
fun patrol [waypoints]
    loop wp in waypoints
        print "moving to {wp}"
        yield wait 1.0
    end
    print "patrol complete"
end
```

```csharp
var script = new WarScriptLanguage("patrol", File.ReadAllText("patrol.ws"), null,
                                   (s, msg) => Console.WriteLine(msg));
WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
script.Run();                                   // 1. compile + execute top level

script.StartCoroutine("patrol", new[]           // 2. start — runs up to the first yield
{
    WarValue.FromArray(new List<WarValue>
    {
        WarValue.FromText("A"), WarValue.FromText("B"),
    })
});

var dt = F64.Ratio(1, 60);
while (script.ActiveCoroutineCount > 0)         // 3. resume once per tick
    script.TickCoroutines(dt);
```

Output: `moving to A`, then `moving to B` about a second later, then `patrol complete`.

## Gotchas and Limitations

**Syntax**

- `{` and `}` are **array** delimiters, not block delimiters. Blocks end with `end`.
- Array indexing is `arr{i}`; function arguments use `[` and `]`; property access is `::`.
- `grid{0}{1}` does not chain — assign the inner array to a temporary first.
- There is no postfix call on an index expression (`arr{0}[args]`) — use a temporary.
- A `fun` nested inside another `fun` is not callable from the outer body; use a lambda value.
- `this` outside a class method throws a host-level exception rather than a script error.
- A raw `"""..."""` literal cannot contain a run of three quotes, and does not interpolate.
- An unknown escape sequence, an unterminated literal, and an unterminated `{` interpolation are
  all syntax errors — they used to be accepted silently.
- Escapes changed the meaning of a backslash inside a literal: `"C:\new"` used to be the six
  characters it looks like, and is now `C:` followed by a line break and `ew`. Double it —
  `"C:\\new"` — or use a raw literal.
- `""""` (four adjacent quotes) used to be two empty literals; it now opens a raw literal.

**Semantics**

- Reading an undefined variable, or an out-of-range array index, yields `null` silently.
- Lambdas have no closures — parameters and globals only.
- Default parameters cannot distinguish an omitted argument from an explicit `null`.
- Functions are keyed by `(name, arity)`; native functions require an exact arity match.
- Comparison coerces to text across types, so `1 == "1"` is `True`.
- Booleans print as `True`/`False`, and arrays print as `[1, 2, 3]` even though the literal is `{1, 2, 3}`.

**Numerics**

- Every number is `F64` fixed point. `sqrt [25]` is `5.000000085681677`, not `5`; compare with a
  tolerance in tests.
- Integer part range ±2147483647; fractional digits past 9 are truncated.

**Host**

- `Call()` returns `void` — pass results back through a global.
- Register libraries and bindings **before** `Run()`.
- `LexicalParser` caches globally by source string; call `LexicalParser.ClearCache()` for hot reload.
- `Reload()` and `LoadBytecode()` stop all coroutines and invalidate previously obtained
  `FunctionDefinition` handles.
- `MemoryBudget` is not reliably catchable from script (see [Safety budgets](#safety-budgets)).

## License

See [LICENSE](LICENSE.md) for details.
