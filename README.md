<p align="center">
  <img src="warscript-logo-wordmark.png" alt="WarScript" width="520">
</p>

<p align="center">
  A lightweight, embeddable scripting language with an interpreter written in C#.
</p>

---

WarScript is a custom scripting language designed to be easily embedded into C# applications and Unity projects. It provides a simple yet expressive syntax for scripting game logic, automation, and runtime behavior without recompiling your project.

All numbers are deterministic 32.32 fixed-point (`FixMath.F64`) rather than floating point, so the same script produces bit-identical results on every platform — built for lockstep-networked games.

## Installation

### Unity (UPM)

Open **Window → Package Manager → + → Add package from git URL** and enter:

```
https://github.com/nine9123/WarScript.git#upm
```

### C# Projects

Clone or add as a submodule:

```bash
git clone https://github.com/nine9123/WarScript.git
```

## Quick Start

```csharp
var script = new WarScriptLanguage(
    "example",
    sourceCode,
    fileResolver: null,
    logger: (s, msg) => Debug.Log(msg));

WarScriptLibraryRegistry.RegisterAll(script, script.GlobalDefinitionScope);
script.Run();

var tick = script.GetFunction("tick", 1);
script.Call(tick, WarValue.FromNumeric(F64.FromFloat(deltaTime)));
```

## Language Guide

### Variables

Variables are dynamically typed. No declaration keyword — just assign.

```ruby
x = 42
name = "hello"
active = true
data = null
```

### Types

WarScript has 7 value types: **Numeric** (`F64` — deterministic 32.32 fixed-point, *not* floating point), **Logical** (true/false), **Text** (string), **Array**, **Class**, **Null**, and **NativeObject** (C# objects exposed to scripts).

Fractional literals like `0.5` and `99.5` are written normally and are exact where representable in fixed-point; values such as `0.7` are deterministically truncated. `Math` transcendentals (`sqrt`, `pow`, `sin`, …) are fixed-point approximations.

### Operators

```ruby
# Arithmetic
x = 10 + 3    x = 10 - 3    x = 10 * 3    x = 10 / 3    x = 10 % 3

# String operations
"hello" + " world"       # "hello world"
"hello world" - "world"  # "hello "
"ab" * 3                 # "ababab"

# Comparison
x == y    x != y    x < y    x <= y    x > y    x >= y

# Logical
a and b    a or b    !a

# Compound assignment
x += 1    x -= 1    x *= 2    x /= 2
```

### Arrays

Arrays use `{}` for literals and `{index}` for access (zero-indexed).

```ruby
arr = {1, 2, 3}
arr{0}             # → 1
arr << 4           # append → {1, 2, 3, 4}
arr{1} = 99        # set → {1, 99, 3, 4}
```

### Conditionals

```ruby
if health > 50
    print "healthy"
elif health > 0
    print "wounded"
else
    print "dead"
end
```

### Loops

```ruby
# While loop
loop health > 0
    health -= 10
end

# For loop (exclusive upper bound)
loop i in 0..10
    print i
end

# For loop with step
loop i in 0..100 by 5
    print i
end

# Foreach loop
loop item in inventory
    print item
end

break    # exit loop
next     # skip to next iteration
```

### Functions

Arguments are passed in `[]`. Called with `[]`.

```ruby
fun add [a, b]
    return a + b
end
result = add [3, 4]    # → 7
```

#### Default Parameters

Trailing parameters can have default values.

```ruby
fun greet [name, greeting = "Hello"]
    return greeting + ", " + name
end
greet ["World"]          # → "Hello, World"
greet ["World", "Hi"]    # → "Hi, World"
```

#### Named Arguments

```ruby
fun create_unit [name, hp, team]
    print "{name} ({hp} HP) on team {team}"
end
create_unit [team: "Red", name: "Soldier", hp: 100]
```

### Lambda / First-Class Functions

Functions are values. Use `fun [params] body end` in any expression position.

```ruby
double = fun [x] return x * 2 end
print double [5]    # → 10

# Pass as argument
fun apply [arr, func]
    result = {}
    loop item in arr
        result << func [item]
    end
    return result
end
doubled = apply [{1, 2, 3}, fun [x] return x * 2 end]

# Return from a function
fun pick_strategy [mode]
    if mode == "aggressive"
        return fun [unit] return unit :: attack [] end
    else
        return fun [unit] return unit :: defend [] end
    end
end

# Store in arrays
ops = {fun [a, b] return a + b end, fun [a, b] return a * b end}
f = ops{0}
print f [3, 4]    # → 7
```

### Constants

Immutable global values. Reassignment raises a syntax error at parse time.

```ruby
const MAX_HP = 100
const TEAM_NAME = "Red"
const HALF_HP = MAX_HP / 2    # expressions evaluated at runtime
```

### Enums

Named numeric constants grouped under a class instance. Access with `::`.

```ruby
enum DamageType
    PHYSICAL
    MAGICAL
    TRUE = 5
end

DamageType :: PHYSICAL                          # → 0
DamageType :: name [DamageType :: PHYSICAL]     # → "PHYSICAL"
DamageType :: values                            # → {0, 1, 5}
DamageType :: names                             # → {"PHYSICAL", "MAGICAL", "TRUE"}
DamageType :: count                             # → 3

# Loop over all members
loop v in DamageType :: values
    print DamageType :: name [v]
end
```

Auto-increment from last value. Explicit values reset the counter:

```ruby
enum Priority
    LOW = 10     # 10
    MEDIUM       # 11
    HIGH         # 12
    CRITICAL = 100  # 100
    EMERGENCY    # 101
end
```

### Classes

```ruby
class Point [x, y]
    fun magnitude []
        return Math_sqrt [this :: x * this :: x + this :: y * this :: y]
    end
end

p = new Point [3, 4]
print p :: x             # → 3
print p :: magnitude []  # → 5
```

#### Inheritance

```ruby
class Animal [name]
    fun speak [] return this :: name end
end
class Dog [name] : Animal [name]
    fun speak [] return this :: name + " says woof" end
end
```

Multiple inheritance: `class Student [email, name] : User [email], Person [name] end`

Type checking: `d is Dog` → true, `d as Animal` → cast or null

### Exception Handling

```ruby
begin
    raise "something went wrong"
rescue err
    print "Caught: " + err
ensure
    print "always runs"
end
```

### String Interpolation

```ruby
print "Hello {name}, you are {age} years old"
```

### Coroutines

```ruby
fun patrol []
    print "moving to point A"
    yield wait 2.0
    print "moving to point B"
    yield wait 2.0
end
```

Yield types: `yield` (next tick), `yield wait N` (N seconds), `yield until condition`.

### Import

```ruby
import "utils"
import "ai/patrol"
```

### Standard Library

**Math**: `Math_sqrt`, `Math_pow`, `Math_floor`, `Math_ceil`, `Math_round`, `Math_clamp`, `Math_lerp`, `Math_abs`, `Math_min`, `Math_max`, `Math_sign`, `Math_sin`, `Math_cos`, `Math_atan2`, `Math_PI`

**Array**: `Array_length`, `Array_remove_at`, `Array_remove`, `Array_contains`, `Array_index_of`, `Array_clear`, `Array_pop`, `Array_insert`, `Array_copy`

**Coroutine**: `Coroutine_start`, `Coroutine_stop`, `Coroutine_stop_all`

### Numeric Separators

```ruby
big = 1_000_000
precise = 3.141_592_653
```

## C# Binding

### Attribute-Based (Recommended)

```csharp
[WsModule("combat", Description = "Combat system")]
public static partial class CombatModule
{
    [WsEnum] public enum DamageType { Physical, Magical, True = 5 }
    [WsConst] public const int MAX_HP = 100;
    [WsConst] public const float CRIT_MULTIPLIER = 2.5f;

    [WsFunction("deal_damage")]
    public static F64 DealDamage(F64 amount, F64 type) => amount;
}
```

Run **WarScript → Generate Bindings** in Unity.

> Function parameters and return types must be `F64` (or `int`/`bool`/`string`/`WarValue`/`List<WarValue>`) — `double`/`float` are rejected at generation time. `[WsConst]` values may still be `float`/`double` (baked to a fixed-point raw during generation). An `F64` parameter can't have a C# default; for an optional numeric, take a `WarValue x = default` and read `x.IsNumeric ? x.Numeric : fallback`.

### Manual Binding

```csharp
scope.AddFunction(new NativeFunctionDefinition(
    new FunctionDetails("my_func", new List<string> { "a", "b" }),
    args => WarValue.FromNumeric(
        NativeHelper.NumericArg(args, 0) + NativeHelper.TextArg(args, 1).Length),
    "Description", "NumericValue"));
```

## Safety Features

```csharp
script.InstructionBudget = 100_000;  // max bytecode instructions per Run()/Call()
script.MemoryBudget = 1_000_000;     // max heap bytes per Run()/Call()
```

Both raise catchable exceptions handled with `begin/rescue`.

## Hot Reload

```csharp
script.Reload(newSource);
tickFunc = script.GetFunction("tick", 1);
```

Global variables survive. Definitions updated. Coroutines stopped.

## License

See [LICENSE](LICENSE.md) for details.
