# WarScript language reference (v2)

This is the normative description of the WarScript language as implemented
after Phase 3 of the v2 plan (`WARSCRIPT_V2_PLAN.md`, section 5, from which it
was written in T3.10). The `README.md` "Language Guide" is the tutorial; this
document is the reference: where the two disagree, this one is meant to be
right, and a disagreement is a bug in one of them. Plan IDs (DEC-n, PROP-n, Dn)
are given where a rule was decided, so the reasoning can be looked up.

Every rule here that can be shown by running a script has a golden test under
`Tests/Golden~/` (named in brackets where it is not obvious).

## 1. Lexical structure

- **Comments** start with `#` and run to the end of the line.
- **Lines.** A statement ends at the end of its line. `\r\n`, `\n` and a lone
  `\r` each end one line. Columns count from 1 in UTF-16 code units, so a tab is
  one column.
- **Blocks** end with `end`. `[ ]` are array delimiters (literals and indexing),
  never blocks. `( )` delimit parameter and argument lists, and group
  expressions: a `(` right after a name or `fun` (on the same line, blanks
  allowed) opens a list (`f(a)`, `fun f(a)`, `fun(x)`, `new P(1)`, `obj.m()`),
  any other `(` groups (T3.12), as does one after `yield wait` or
  `yield until` (`yield until (x > 0)`). `{ }` appear only in text interpolation; `{` or
  `}` elsewhere is a syntax error.
- **Members** are `obj.name`, `obj.method(args)` and `obj.new Nested(args)`.
  A `.` right after a number followed by a name is a member (`5.x`), not a
  decimal point.
- **Numbers**: `'-'? digit+ ('.' digit+)?`, with `_` allowed as a digit separator
  (`1_000_000`). See section 2.
- **Text literals**: `"..."` and `$"..."` (identical: escapes and interpolation)
  and `"""..."""` (raw). Escapes are `\"` `\\` `\{` `\}` `\n` `\t` `\r`; any other
  `\` sequence is a syntax error. `"a {expr} b"` interpolates: the literal is
  `("a " + (expr) + " b")`, and a literal that starts with a hole starts with
  `""`, so an interpolation is always text. A raw literal has no escapes and no
  interpolation, may span lines (the line breaks right after the opening and
  right before the closing delimiter are dropped), closes on the last three
  quotes of a run, and cannot contain `"""`. `$"""` is rejected.
- **`@name`** is lexed as `this.name`, all three tokens at the `@`'s column.
  `@` followed by anything but a name (a keyword, a literal, a space) is a syntax
  error.
- **Reserved words** include `fun`, `class`, `new`, `this`, `return`, `if`, `elif`,
  `else`, `end`, `loop`, `in`, `by`, `break`, `next`, `begin`, `rescue`, `ensure`,
  `raise`, `yield`, `import`, `const`, `enum`, `global`, `is`, `as`,
  `and`, `or`, `true`, `false`, `null`, `print`, `assert`. (`wait` and `until`
  have their meaning only after `yield`.)

## 2. Values

| Type | Description |
|---|---|
| null | The absence of a value. |
| number | A signed 32.32 fixed-point value (`F64`). There is no floating point anywhere in the language. |
| logical | `true` or `false` |
| text | An immutable UTF-16 string. Compared ordinally. |
| array | Mutable, zero-based, with reference semantics. |
| instance | A class instance, with reference semantics. |
| native | An opaque host reference: `F64Vec3`, function values (lambdas) and host objects. Scripts pass them around but cannot look inside. |

**Numbers.** A literal is parsed with integer arithmetic only, so its value is
bit-identical on every platform. Values exactly representable in 32.32 (`0.5`,
`99.5`) are exact; others (`0.7`) are truncated deterministically. Fractional
digits past the ninth are truncated; the integer part ranges over
±2,147,483,647. A malformed literal is a syntax error. Converting a number to an
integer (an index, a repeat count, a native's `int` parameter) truncates toward
zero (D1). Overflow behaves as the fixed-point library does (wrapping for `+`
and `-`). The math library's transcendental functions (`sqrt`, `pow`, `sin`,
...) are deterministic approximations, not bit-exact real math; `round` rounds
half up.

**Truthiness.** `null`, `false` and the number `0` are falsy. Everything else is
truthy, including `""`, empty arrays and instances.

## 3. Operators

Precedence follows the usual arithmetic rules; parentheses group.

| Expression | Result |
|---|---|
| `n + n` | Addition |
| `text + x`, `x + text` | Concatenation, `x` formatted as in section 10 (`"b=" + true` is `b=true`) |
| `array + array` | A new array, the left elements then the right |
| `array + x`, `x + array` (`x` neither array nor text) | TypeError (PROP-6) |
| `n - n` | Subtraction. Text has no `-`: use `Text_remove(s, sub)` (PROP-7) |
| `n * n` | Multiplication |
| `text * n`, `n * text` | Repetition. The count truncates toward zero; a count of 0 or less gives `""` |
| `n / n`, `n % n` | Division and remainder (the remainder has the sign of the left operand: `-7 % 3` is `-1`). A zero right operand raises DivisionByZero (`Division by zero`, `Modulo by zero`, D4) |
| `-x` | Negation, numbers only |
| `!x` | `true` when `x` is falsy, else `false` |
| `a and b`, `a or b` | Short-circuit; the result is the operand that decided (`1 and 2` is `2`, `null or "x"` is `x`) (PROP-23) |
| `<`, `<=`, `>`, `>=` | Two numbers, or two texts compared ordinally (`"B" < "a"`). Anything else, logical values included, is a TypeError (PROP-17) |
| `==`, `!=` | Section 4. Values of different types are never equal, and comparing them raises nothing |
| `array << x` | Appends `x`; the result is the array itself |
| `obj is C` | `true` when `obj` is an instance of `C` or of a class deriving from it, else `false` (any value) |
| `obj as C` | `obj` when `obj is C`, else `null` |

`+` decides in this order: if either operand is text, it concatenates; otherwise,
if both are numbers, it adds; otherwise, if both are arrays, it joins them;
otherwise it raises TypeError.

Every other combination raises TypeError with the message
`Cannot apply '<op>' to <type> and <type>` (unary: `Cannot apply '-' to <type>`),
for example `Cannot apply '-' to text and number`. Type names: `null`, `number`,
`logical`, `text`, `array`, `instance of <Class>`, `native <TypeName>`. There is
no implicit conversion between types (DEC-8).

Results longer than `MaxTextLength` (text `+` and `*`) or `MaxArrayLength`
(array `+`, `<<`, array literals) raise LimitExceeded (`Text too long`,
`Array too long`), checked before anything is allocated.

Compound assignment `x += e` (`-=`, `*=`, `/=`) means `x = x + e`, so the target is evaluated twice: `g()[0] += 1` calls `g` twice, and `a[i()] += 1` calls `i` twice.

[Goldens: `language/operator_types.ws`, `language/operator_table.ws`,
`language/text_length_limit.ws`, `errors/division_by_zero.ws`,
`errors/modulo_by_zero.ws`; the array limits are `AllocationGuardTests`.]

## 4. Equality

- `null == null` is true, and null equals no other value.
- Numbers compare by their raw fixed-point bits; logical values by value; text
  ordinally.
- Arrays compare structurally, element by element. A comparison nested deeper
  than `MaxValueDepth` (256) raises LimitExceeded (`Value nested too deeply`);
  this is what comparing two cyclic arrays does.
- Instances compare by identity of the instance that was created: an instance
  and the same instance seen through a cast (`d as Base`) are equal; two
  instances with equal fields are not (DEC-9, PROP-8).
- Native objects compare with the host's equality: a function value equals
  itself (two lambdas with the same code written twice are different values),
  an `F64Vec3` compares by value.
- `!=` is always the negation of `==`.

[Goldens: `language/equality.ws`, `language/operator_types.ws`,
`language/instance_identity_diamond.ws`, `language/value_depth.ws`.]

## 5. Indexing, text and properties

**Arrays.** `arr[i]` needs a number `i`, truncated toward zero (D1); any other
index type is a TypeError (`Index must be a number, not text`), and an index
below 0 or not below the length is an IndexError
(`Index 3 is out of bounds (length 3)`). `arr[i] = v` follows the same rules.
Arrays never grow on assignment: append with `<<`.

**Text.** `text[i]` is a one-character text, with the same rules. `text[i] = v`
is a TypeError (`Cannot assign to an index of text: text is immutable`, DEC-10).
Indexing anything else is a TypeError (`Cannot index number`).

Any operand can be indexed, and indexes chain (T3.12): `grid[0][1]`,
`f(x)[0]`, `obj.items[0][1]`, `"abc"[1]`, `(a + b)[0]`; each of them can also
be assigned to (`grid[0][1] = v`). A member binds tighter than an index, so
`obj.items[0]` indexes `obj.items`. There is no call of a call's or an
index's result: `arr[0](args)` and `f(x)(y)` are syntax errors (`only a name can be called`); put the value in a variable first.

**Iteration.** `loop v in x` iterates the elements of an array, or the
constructor parameters of an instance in order. Anything else is a TypeError
(`Unable to iterate ...`).

**Properties.** `obj.name` reads a property of an instance and
`obj.name = v` writes one. On anything that is not an instance both are a
TypeError (`Cannot access property 'p' on non-class value`,
`Cannot set property 'p' on non-class value`). A property the instance's classes
do not declare is a NameError (`'z' is not a field of class 'P'`, PROP-15). The
declared properties are the constructor parameters plus the fields of the class
body (section 7), base classes included; a declared field not assigned yet reads
`null`.

[Goldens: `language/indexing.ws`, `errors/index_out_of_bounds.ws`,
`errors/index_text_write.ws`, `language/instance_iteration.ws`,
`errors/iterate_non_iterable.ws`,
`errors/property_on_non_class.ws`, `errors/undeclared_property_read.ws`,
`errors/undeclared_property_write.ws`, `language/declared_properties.ws`.]

## 6. Statements, variables and scope

### Statements

```ruby
x = 1                         # assignment; += -= *= /=
if c ... elif c ... else ... end
loop c ... end                # while c is truthy
loop i in a..b ... end        # i = a, a+1, ... while i < b
loop i in a..b by s ... end   # step s, which must be greater than 0
loop v in arr ... end         # each element (or each constructor parameter of an instance)
break / next                  # leave / continue the innermost loop
fun name(a, b = 1) ... end    # function; trailing parameters may have defaults
return value                  # the value is optional
begin ... rescue e ... ensure ... end
raise value
yield / yield wait seconds / yield until condition
print value / assert condition
const NAME = value
enum Name ... end
class Name(params) : Base(args) ... end
import "path"
global a, b
```

**Range loops.** The bounds and the step are evaluated once, before the first
iteration, and the loop runs while the variable is less than the upper bound
(the first comparison follows the `<` rules of section 3). A step that is not
greater than 0 would never run the body or never end, so it is an error: at
compile time when it is a number literal or a `const` declared as one, else an
ArgumentError when the loop starts
(`The step of a range loop must be greater than 0, not 0: ...`).
[Goldens: `language/range_step_not_positive.ws`, `language/range_step_const.ws`,
`errors/range_step_dynamic.ws`.]

**Default parameters.** A parameter with a default receives it when the argument
is omitted *or* passed as `null`: there is no way to tell the two apart. A
function with defaults can be called with any number of arguments from its
required ones to all of them.

**Named arguments.** `f(team: "Red", hp: 100)` passes arguments by parameter
name. Naming a parameter twice, or one the function does not have, is a compile
error when the function is known at compile time.

**Lambdas.** `fun(x) return x * 2 end` in expression position is a function
value. A lambda sees its own parameters and locals and the globals; reading a
local of the code around it is a compile error (`closures are not supported`).
Call one held in a variable with `f(args)`. Function values print as
`<fun lambda>`.

### Top level

Assignments at the top level of a file create or update globals of the
instance. Blocks (`if`, loops, `begin`) do not open a scope (DEC-4): a variable
assigned in a top-level block is a global. Every file imported into an instance
shares one global namespace, and host globals (constants and enums the host
registers) are visible to every file. Constants and enum members cannot be
reassigned; this is checked at compile time across the whole module set.

### Functions, methods and lambdas

- Parameters are locals.
- A name assigned anywhere in the body, and not declared `global`, is a local of
  the whole body (PROP-9), stored in a stack slot.
- Reading a local before its first assignment in the source is a compile error.
- Loop and rescue variables exist only inside their block; naming one like a
  parameter, local, declared global or enclosing loop/rescue variable of the
  same body is a compile error.

**`global a, b`** declares that this body's assignments to `a` and `b` write the
instance globals (creating them). It is allowed in function, method, lambda and
class bodies, must come before any use of those names in the body, cannot name a
parameter, and is a compile error at the top level (PROP-10). Reading a global
needs no declaration.

```ruby
count = 0                 # global

fun bump(n)
    global count          # required to write the global
    total = n * 2         # local to bump
    count = count + total
    return total
end

fun peek()
    return count          # reading a global needs no declaration
end
```

### Name resolution

**Reads.** A bare name is, in order: a loop or rescue variable in scope; a
parameter or local; a global, looked up at run time. A global no one has
assigned is a NameError (`undefined variable 'x'`); a global assigned `null` is
defined.

**Calls.** In `f(args)`: if `f` is a local or parameter, the call goes through
the function value it holds. Otherwise the function named `f` that takes that
many arguments is called, searched in the module's definitions (in a method,
the class's methods first) and the natives. Otherwise a global holding a
function value is called. Anything else is a NameError
(`Function 'f' with 1 args is not defined`); calling a value that is not a
function is a TypeError (`Value is not callable`).

[Goldens: `language/break_outside_loop.ws`, `language/next_outside_loop.ws`,
`language/function_locals.ws`, `language/global_declaration.ws`,
`language/names_resolve_by_frame.ws`, `language/read_before_assignment.ws`,
`language/lambda_closure.ws`, `errors/undefined_variable.ws`,
`errors/undefined_function.ws`, `errors/not_callable.ws`.]

## 7. Classes, fields and enums

```ruby
class Unit(name, hp)
    armor = 0                     # a field
    fun hit(n)
        @hp -= n - @armor         # @x is this.x
    end
end

class Archer(name, hp) : Unit(name, hp)
end

a = new Archer("robin", 50)
a.hit(10)
print a.hp
```

**Fields** are the constructor parameters plus every name the class body assigns
(bare or as `@x = ...`) and does not declare `global`, plus the fields of the
base classes. All class parts of one instance share one set of fields. A class
passes base classes their arguments by naming its own constructor parameters
(`: Unit(name, hp)`), one base or several (multiple inheritance).

**`new C(args)`** first allocates the whole instance, then runs the constructor
bodies: the base classes depth-first in declaration order, then the class
itself. The number of arguments must equal the number of constructor parameters
(compile error when the class is known, else an ArgumentError). A class that
inherits from itself is an error at `new`. A base parameter bound to two
different arguments (`class C(p, q) : A(q, p), B(p, q)` where `A` and `B`
both name their parameters `a, b`) is an error.

**The class body** runs as the constructor. In it, a bare name reads a field of
the instance and falls back to a global; a bare assignment declares (and
writes) a field unless the name is declared `global`; `@x` and `this.x` may
also be used; `yield` is a compile error (PROP-21).

**Methods.** Bare names follow the function rules (section 6): a method never
reads a field by its bare name. `@x` reads or writes field `x` of `this` and is
checked at compile time against the fields of the class and its bases.
`obj.x` on another instance is checked at run time (NameError for an unknown
property); a name that is a field or method of no class at all is already a
compile error (PROP-29). `obj.m(args)` calls a method (`Method 'm' with 0
args not found` when the class has none of that arity).

**Lambdas** cannot use `this` or `@x` (PROP-16); neither can code outside every
class.

**Casts.** `obj is C` and `obj as C` (section 3); the class must exist.

**Enums.**

```ruby
enum DamageType
    PHYSICAL          # 0
    MAGICAL           # 1
    TRUE = 5
end
DamageType.PHYSICAL      # 0
DamageType.name(5)      # "TRUE"
DamageType.values        # {0, 1, 5}
DamageType.names         # {"PHYSICAL", "MAGICAL", "TRUE"}
DamageType.count         # 3
```

An enum is a constant holding a single instance whose fields are its members.
Its name and its members cannot be assigned (`Cannot assign to 'X' of enum 'E':
enum members are constant`), a member it does not have is a compile error, and
a repeated member is a compile error.

**Constants.** `const NAME = value` declares a global that cannot be reassigned
anywhere in the module set (`Cannot reassign constant 'NAME'`).

[Goldens: `language/test_classes_*.ws`, `language/class_body_names.ws`,
`language/base_class_fields.ws`, `language/constructor_frames.ws`,
`language/at_field.ws`, `errors/constructor_arity.ws`,
`errors/enum_member_write.ws`, `language/enum_member_unknown.ws`.]

## 8. Imports and module initialization

`import "path"` statements must come before every other statement of a file
(comments may come first); an import anywhere else is a compile error. Paths are
resolved by the host when the module set is compiled or loaded. An import cycle
is a compile error naming the cycle (`Circular import: a -> b -> a`), and so is a
path the host cannot resolve (`Import 'x' not found`).

**Initialization order.** Each file's top-level code runs exactly once per
instance, imports first: a depth-first post-order over the imports, in textual
order. `yield` while an import's top-level code runs is a TypeError.

**One namespace.** The functions and classes of every file of the module set
form one namespace per instance. A second definition of a function name whose
arities overlap an existing one, or a second class of the same name, is a
compile error (PROP-12), and so is a function with the name and an arity of a
native.

[Goldens: `modules/import_order.ws` (initialization order and one namespace,
with `order_left.ws`, `order_right.ws`, `order_base.ws`), `modules/cycle_a.ws`,
`modules/import_not_found.ws`, `modules/duplicate_across_files.ws`,
`language/import_after_statement.ws`, `language/import_in_block.ws`,
`language/duplicate_definitions.ws`, `language/function_shadows_native.ws`;
bytecode imports, re-running a failed import and `yield` in an import:
`ImportTests`.]

## 9. Errors

Every runtime error has a kind:

| Kind | Raised for |
|---|---|
| Raised | `raise value`. The value may be of any type; `raise null` raises the text `Empty exception`. |
| TypeError | Operand types (section 3), indexing (section 5), property access on a non-instance, calling a value that is not a function, iterating a value that is not iterable, assigning an enum member, invalid native arguments |
| IndexError | An index out of bounds |
| NameError | An undefined variable, an undeclared property, a function, method or class missing at run time |
| ArgumentError | Wrong number of arguments to `new`, a range step not greater than 0, invalid native arguments |
| DivisionByZero | `x / 0`, `x % 0` |
| AssertionFailed | A failing `assert` |
| NativeError | Any other failure inside a native function (`Native function 'f' failed: ...`) |
| InstructionBudgetExceeded | The host's instruction budget is used up; it stays exhausted until the host's next entry, so rescuing it does not continue the script |
| MemoryBudgetExceeded | The host's memory budget is used up |
| LimitExceeded | The value stack, call frames (`Stack overflow`), exception handlers, host nesting, text length, array length or value depth exceeded a limit (section 12) |

**Rescuing.** `begin ... rescue e ... ensure ... end` rescues every kind above.
`e` is the raised value for `raise`, and the message text for every other kind
(PROP-22). The `ensure` block runs however the block is left: normally, by an
error, by `return`, `break` or `next`; a `raise` or `return` in the `ensure`
block replaces what was pending.

**Errors through natives.** An error that leaves a native function that called
back into the same instance continues in the calling script, at the native's
call site (PROP-1).

**Unhandled errors.** An error that escapes the host's outermost entry halts the
instance until the host clears it. The host sees the kind, the message, the
line and column, and the script call stack.

[Goldens: one per kind in `errors/`; `raise null`: `errors/raise_null.ws`;
the rescued value: `errors/rescue_value_is_message.ws`.]

### Compile-time checks

The compiler reports whatever it can prove before anything runs (DEC-21); the
runtime errors above stay as the backstop for values known only at run time. A
compile reports every error it finds (it recovers at statement and definition
boundaries), then the warnings.

Errors:

- syntax errors, and the limits of section 12;
- `break` or `next` outside a loop;
- a call of a name that is no function, native or variable of the module set,
  or with a number of arguments none of its definitions takes;
- a read of a name that is not a local and that the module set never assigns
  and the host does not provide (`undefined variable 'x'`, PROP-27);
- an operator, index or property operation whose operand types are known
  statically (from literals, `const`s and enum members) and rejected (PROP-28):
  the same message the run time would raise;
- `new` of an unknown class or with the wrong number of arguments; `is`/`as` of
  an unknown class; a repeated parameter or enum member; a named argument given
  twice or naming no parameter; `obj.x` where no class has `x`; an unknown
  enum member; `this` or `@x` outside a class or in a lambda; `@x` that is not a
  field of the class (PROP-29);
- the scope errors of section 6 and the definition errors of section 8;
- an assignment to a constant or enum anywhere in the module set.

Warnings (errors when the host compiles with warnings as errors, PROP-31):

- `local 'x' is assigned but never read` (PROP-26);
- `unreachable code after 'return'` (or `'break'`, `'next'`, `'raise'`).

## 10. Formatting

`print`, text concatenation and interpolation format values the same way, on
every platform and in every culture:

| Value | Formatting |
|---|---|
| null | `null` |
| logical | `true`, `false` (DEC-11) |
| integer number | Digits with an ASCII `-` sign |
| fractional number | The shortest decimal of at most 9 fractional digits that reads back as the same value (`0.1`, `3.5`); else the exact decimal expansion (`1 / 3` prints `0.33333333325572311878204345703125`) |
| text | The text itself |
| array | `[a, b, c]`, elements formatted recursively; an array already being formatted, or nested deeper than `MaxValueDepth`, prints `[...]` |
| instance | The class name |
| function value | `<fun name>`; a lambda `<fun lambda>` |
| `F64Vec3` | `(x, y, z)`, using the number rules |
| other native object | `<TypeName>`, or the host's registered formatter |

## 11. Coroutines

The host starts a coroutine from a function; it runs at once until its first
`yield`. Each host tick then resumes the ready coroutines, newest first
(PROP-24):

- `yield` waits for the next tick;
- `yield wait seconds` waits until the ticks' accumulated time reaches it (the
  host supplies each tick's time; there is no clock);
- `yield until condition` checks the condition each tick.

A looping coroutine starts again when it completes. Budgets reset at each
resume. A coroutine's unhandled error ends it and halts the instance (PROP-3).
Scripts start coroutines with the `start_coroutine` native.

## 12. Limits

| Limit | Value | Error |
|---|---|---|
| Value stack | 1,024 slots | LimitExceeded (`Value stack overflow`) |
| Call frames | 128, constructor frames included | LimitExceeded (`Stack overflow`) |
| Nested `begin` blocks with an open handler | 32 | LimitExceeded |
| Nested host entries | 32 | LimitExceeded (`Host nesting limit exceeded`) |
| Parameters of a function, arguments of a call or `new` | 255 | Compile error |
| Constants per function | 65,535 | Compile error |
| Bytecode per function | 65,535 bytes | Compile error |
| Elements of an array literal | 65,535 | Compile error |
| Text length (`MaxTextLength`) | 1,048,576 characters, host-configurable | LimitExceeded (`Text too long`) |
| Array length (`MaxArrayLength`) | 1,048,576 elements, host-configurable | LimitExceeded (`Array too long`) |
| Value nesting (`MaxValueDepth`) | 256 | LimitExceeded for equality; `[...]` when formatting |

## 13. Determinism

The language has no source of nondeterminism: no floating point, no random
numbers (the host supplies a deterministic random module), no clock (the host
supplies each tick's time), culture-invariant formatting and ordinal text
comparison. The same script with the same host inputs produces bit-identical
results on every platform.
