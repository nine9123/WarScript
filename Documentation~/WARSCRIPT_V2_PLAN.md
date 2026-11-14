# WarScript v2: Design Decisions and Implementation Plan

| | |
|---|---|
| Document version | 1.1, 2026-11-12 (round 4: DEC-20, DEC-21); task tracker (Appendix E) kept current by the implementer |
| Owner | Oleg Dammer |
| Package | `com.nine9.warscript` 0.5.0, Unity 6000.3 |
| Implementer | Claude Code, working in the WarScript repository |
| Scope | Phases 0 to 3 are approved for implementation. Phase 4 (performance) and multi-VM work (pools, shared cores) are deferred. |
| Location | `Documentation~/WARSCRIPT_V2_PLAN.md`, with a one-line pointer to it from the repository's `CLAUDE.md`. The `~` suffix keeps Unity from importing the folder. |

---

## 0. How to use this document

This document is the hand-off from a design review held in chat between the owner and Claude. It records what the review found, which options were discussed, what the owner decided, and how to implement it, in enough detail that an implementer who was not part of the conversation can execute the plan without guessing.

Read sections 0 to 3 first: the ground rules, the context, and the decisions. Section 4 (findings with evidence) and sections 5 and 6 (the target language and host API) are reference material for the tasks in section 7. Sections 8 to 10 hold detailed specifications for the migration, the test infrastructure and the bytecode format. Section 11 lists deferred work, section 12 lists open questions that need the owner's answer, and the appendices contain the repro corpus, benchmark data, Unity `.meta` templates, a file map and a task tracker.

Identifiers used throughout:

| Prefix | Meaning |
|---|---|
| `CRASH-n`, `BUDGET-n`, `DET-n`, `SEC-n`, `SEM-n`, `API-n`, `DX-n`, `PERF-n` | Review findings (section 4) |
| `DEC-n` | Decisions made by the owner (section 3.1) |
| `PROP-n` | Details proposed by this document that were not discussed explicitly. Treat them as approved defaults; if implementation shows one is wrong, stop and ask the owner (section 3.2). |
| `T<phase>.<n>` | Implementation tasks (section 7) |
| `F-<area>-n` | Deferred work (section 11) |
| `Q-n` | Open questions for the owner (section 12) |

The existing `CLAUDE.md` uses D1 to D7 for the fixed-point design decisions (truncation semantics, literal parsing, division by zero, and so on). Those remain valid and are referred to here as "CLAUDE.md D1" and so on.

### 0.1 Ground rules

| Rule | Detail |
|---|---|
| R1. Determinism is non-negotiable | Everything in `CLAUDE.md` under "Determinism & Fixed-Point Numerics" still applies: no `float`/`double` in `Runtime/`, no `System.Random`, no wall clock, no `UnityEngine` types. This plan adds three rules. All string formatting uses `CultureInfo.InvariantCulture`. The runtime holds no static mutable state except explicitly synchronized caches. The runtime never calls `ToString()` on arbitrary host objects. |
| R2. Unity compatibility | Runtime code must compile as `netstandard2.1` with C# 9.0, which is what Unity 6 supports. The headless runtime project (T0.1) enforces this, so never raise its `LangVersion`. Test code must stay within the NUnit 3.5 API surface, because the Unity Test Framework bundles an NUnit 3.5 derivative. The headless suite runs the same tests on NUnit 3.14. Do not use NUnit 4, which moved `Assert.AreEqual` and the other classic asserts to `ClassicAssert`, and do not use NUnit features newer than 3.5 such as `Assert.Multiple`. |
| R3. Unity `.meta` files | The package is installed from git, which makes it an immutable UPM package. Every new file or folder outside a `~` folder therefore needs a committed `.meta` file with a fresh random GUID, and deleting a file means deleting its `.meta` as well. Templates are in Appendix C. Prefer `~` folders (`Headless~/`, `Tools~/`, `Documentation~/`, `Tests/Golden~/`) for anything Unity does not need to import. |
| R4. No backward compatibility | There are no third-party mods; the owner is the only WarScript user. Syntax, semantics, the host API, the native-function ABI and the bytecode format may all change. Every `.ws` script is migrated: test resources, inline test sources and the owner's game scripts (section 8). The bytecode loader accepts only the exact current format. |
| R5. Tests first | Every bug fix lands together with a regression test, preferably a golden `.ws` file (section 9.2). Phase 0 must be complete before any Phase 3 semantic change, because Phase 3 is verified against the Phase 0 test suite (goldens, invariants, state hashes), not against the game (DEC-20). |
| R6. Stay in scope | Do not implement Phase 4 or multi-VM work (section 11) unless it falls out of an in-scope task at no extra complexity. Do not change the VM architecture (DEC-1). |
| R7. Keep documentation current | Update `CLAUDE.md` (and `README.md` where it documents behavior) in the same change that alters behavior. Record any new decision in section 3 of this document. |
| R8. Ask rather than invent | When this plan does not cover a semantic question, ask the owner rather than inventing behavior. Commit per task, with the task ID in the commit message. |

---

## 1. Context

### 1.1 What WarScript is

WarScript is an embeddable, dynamically typed scripting language with Ruby-inspired syntax, implemented in C# and shipped as a Unity UPM package. It is the scripting layer of the owner's lockstep-networked game (Warman). Today it runs mostly event glue; custom skills and modifiers are moving into WarScript; an `UpdateTick` hook exists but the core mod uses it lightly. Heavy modder-written loops, such as custom pathfinding in script, are possible in principle but far in the future. The runtime is about 11,000 lines of C#; the test suite has 832 NUnit test methods and 83 `.ws` resource scripts.

The execution pipeline is `LexicalParser` (tokens, with string interpolation desugared at lex time), then `StatementParser` and `ExpressionReader` (AST, with desugaring of default parameters, `const` and `enum`), then `Compiler` (single pass, one `CompiledFunction` per function), then `WarVM` (a stack-based dispatch loop). The AST is discarded after compilation. A legacy tree-walking interpreter (`Execute`/`Evaluate` methods on AST nodes, `Coroutine.cs`, `WarScriptLanguage.CallTreeWalk`) still exists as a fallback; DEC-2 removes it.

The key runtime structures are `WarValue`, a 24-byte tagged struct holding a tag, an `F64` and an object reference; `CompiledFunction` and `Chunk`, which hold bytecode as `List<byte>`, a constant pool, one line number per code byte, and monomorphic inline caches for property access; and several per-instance context stacks on `WarScriptLanguage`: `MemoryContext` (dictionary-backed `MemoryScope`s), `DefinitionContext`, `ClassInstanceContext` and `ExceptionContext`.

### 1.2 Constraints from the host game

Every peer must produce bit-identical results from identical inputs across x86-64 and ARM, on Windows, macOS and Linux (including Steam Deck), under Mono and IL2CPP. Numbers are FixPointCS `F64` values (signed 32.32 fixed point).

Mod content ships as a compiled mod archive that includes WarScript bytecode. The game hashes the whole archive and compares the hashes when a session starts, which already guarantees that all peers run identical bytecode, so no separate bytecode hash is needed (DEC-3). Script errors that halt a script must happen identically on every peer. They do, as long as they come from deterministic causes (section 6.6).

### 1.3 Host topology

The game is organized into maps, and a map contains dozens of rooms. A room is a play area that can create and run its own WarScript instances. Each instance is a `WarScriptLanguage` object with its own VM, globals and definitions. A modder may put all logic in one instance or use several per room. The game also creates bootstrap and one-shot instances that set up rooms or load static mod resources and are then discarded. A running game can therefore hold many instances at once; the assumption "one instance per mod" is wrong.

| Implication | Consequence in this plan |
|---|---|
| Faults must be contained per instance | The halted state belongs to a single `WarScriptLanguage` (DEC-13). What happens to sibling instances in the same room or map is host policy, outside the language (Q-4). The API exposes a `Faulted` event and an `InstanceLabel` so the host can implement whatever policy it wants. |
| Errors must identify their instance | `ScriptError` carries `ScriptName` and a host-supplied `InstanceLabel` (PROP-4). |
| Many instances may coexist | Per-instance overhead (about 178 KB for a trivial script, Appendix B.4) and repeated compile or deserialize work will matter eventually. That work is deferred (F-VM-1 to F-VM-3), but Phase 3 data structures should keep immutable program data separate from per-instance mutable data so that sharing stays possible later. |
| Instances must not interfere with each other | No static mutable state in the runtime (R1, T1.9). |
| Values may cross instances through natives | Undefined today. Q-1 proposes a rule. |
| One-shot instances are common | Errors must be visible from a single `Run()` call (`ScriptResult`, DEC-12). |

### 1.4 How the review was done

The runtime was compiled unmodified against upstream FixPointCS (github.com/XMunkki/FixPointCS at commit `a852f05b428a942f8dc274ee516a893ae224e0d4`, files `FixPointCS/*.cs` and `Examples/FixMath/*.cs`) on .NET 8 (Linux x64, RyuJIT) and exercised with a probe and benchmark harness. It also builds as `netstandard2.1` with C# 9.0, using the project file given in section 9.1.

Findings marked as verified in section 4 were reproduced, and the outputs quoted there are actual outputs. Performance numbers come from RyuJIT, not Mono or IL2CPP, so treat them as relative rather than absolute. Culture-dependent results were verified on .NET 8 with ICU; Unity's Mono culture data was not tested.

---

## 2. Summary of the discussion

**Round 1: review.** The owner asked what could be improved in performance, developer UX, security and general language-engineering practice, and asked for a comparison of stack-based and register-based VMs.

The review found the foundations sound: fixed-point determinism with integer-only literal parsing, instruction and memory budgets, inline caches, source maps and a large test suite. The important problems were not the VM architecture. They were:

- several ways a script can crash the host process or throw C# exceptions out of the VM;
- a bypass of the instruction and memory budgets;
- determinism leaks in number formatting;
- an unverified bytecode loader;
- language semantics that silently do the wrong thing (implicit globals, coercions, silent out-of-bounds access);
- a host API that drops return values and hides errors;
- structural costs (constructor execution, name-based call resolution, per-call native argument lists) that outweigh dispatch overhead.

On stack versus register, a register VM would cut the number of dispatches in tight loops but would fix none of the measured costs, so the recommendation was to stay stack-based.

**Round 2: owner decisions.** The owner accepted all findings except the per-session bytecode hash, which the archive hash already covers. The owner decided:

- `Call` returns a result with a value, and a script error halts the script while the simulation continues;
- the tree-walker is removed completely;
- the stack VM stays and is improved later, for example with superinstructions;
- performance is mostly fine, so the focus is bugs, developer UX and language quirks, with much better tests;
- breaking changes are acceptable because nobody else uses WarScript yet, so every script is migrated to the new semantics (for example a `global` keyword).

The owner also described the workload as mostly event glue, with some gameplay logic moving into WarScript.

**Round 3: plan.** A phased plan was proposed: tests, then crash and security fixes, then the host API, then language changes, then performance. It came with defaults for six language decisions, which the owner accepted. The owner then clarified the host topology (section 1.3) and asked to focus on the language itself before going deep into VM pools or shared cores. This document compiled all three rounds as version 1.0.

**Round 4: no game-side verification, compile-time first (2026-11-12).** After Phase 0 the owner judged the game-side work (audit reports from game sessions, recorded game baselines, reviews of them) to be more than the language needs: WarScript has to stand on its own, which is what the Phase 0 test infrastructure is for. The owner also asked to check as much as possible at compile time. DEC-20 and DEC-21 record this; the T0.8 owner step and T0.9 are dropped, Phase 3 is verified against the test suite and the compiler, and T3.8 is widened (PROP-27 to PROP-31).

---

## 3. Decision log

### 3.1 Owner decisions

| ID | Topic | Options considered | Decision | Notes |
|---|---|---|---|---|
| DEC-1 | VM architecture | (A) keep the stack VM and add superinstructions chosen from measured opcode-pair frequencies; (B) accumulator plus register hybrid, as in V8's Ignition; (C) full Lua-style register VM | A, in Phase 4 | Measured costs are dominated by calls, allocation and dictionary lookups, which a register VM does not fix, and the workload is event glue. C# has no computed goto, so cutting dispatches does matter; superinstructions get most of that benefit for the hot patterns. Revisit only if profiling real scripts shows tight in-script loops dominating, such as modder pathfinding. Changing the instruction set also changes when the instruction budget trips, which is deterministic within a version but shifts behavior across versions. |
| DEC-2 | Legacy tree-walk interpreter | Keep as a fallback; remove | Remove completely (T2.1) | The VM is mature enough to handle everything. Two engines mean every semantic fix has to land twice. |
| DEC-3 | Bytecode hash in the lockstep handshake | Hash the loaded bytecode; rely on the archive hash | Not needed | The mod archive, which contains the bytecode, is hashed as a whole. Format versioning and the verifier are still required (T1.7). |
| DEC-4 | Unit of variable scope | Block scope (a new scope per `if` or loop body); function scope | Function scope. Loop variables and rescue variables (PROP-11) are scoped to their block. Top-level code follows the same rule. | This is already how function bodies behave. Only top-level `if` blocks change: v1 hides variables created inside them (SEM-5). It also removes the `PushScope`/`PopScope` opcodes. |
| DEC-5 | Assignment inside functions | v1 behavior (walk up the scope chain and write any existing variable); locals by default with a `global` declaration, as in Python | Locals by default. Writing a global requires `global name`. Bare reads of globals are allowed. Reading a name that does not exist raises NameError. | Fixes SEM-1, SEM-2 and SEM-3. Full rules in 5.6. |
| DEC-6 | Field access in methods | Implicit `this` resolved at compile time (Java-like); `this :: x` only; `@x` sugar | `@x`, which is sugar for `this :: x`. Inside methods, bare names are locals or globals. The class body remains a field-declaration block, where a bare assignment declares a field. | v1 writes fields through the same scope walk that causes SEM-1. Implicit `this` would need every base-class field at compile time and would keep "a bare name might be something else". Full rules in 5.7. |
| DEC-7 | Imports | Runtime `import` statement (v1); static imports | Static. `import` is allowed only at the top of a file, and imported files are resolved and compiled before the importer. Import cycles are compile errors. | Enables compile-time checks for unknown functions, wrong arity, unknown classes and unknown fields. Rules in 5.8. |
| DEC-8 | Operator typing | v1 coercions; a strict table | The strict table in 5.3 | `text + anything` still concatenates, because string interpolation depends on it. Other mixed-type arithmetic and ordering raise TypeError. `==` across different types is false. |
| DEC-9 | Equality | Structural for class instances (v1); identity | Arrays compare structurally, with a depth limit. Class instances compare by identity (PROP-8 covers casts). | Structural class equality is surprising, and its recursion is a crash risk (CRASH-1). |
| DEC-10 | Silent failures | Keep; raise errors | Raise errors. Out-of-bounds reads and writes raise IndexError. Property access or assignment on a non-instance raises TypeError. Unknown properties raise NameError. Text is immutable, so `s{i} = x` raises TypeError. | Rules in 5.5. |
| DEC-11 | Boolean formatting | `True`/`False` (v1); `true`/`false` | `true`/`false` | Matches the literals. |
| DEC-12 | Host call results and errors | v1 (`Call` returns void; errors are logged and cleared); a result type with halting | `Run`, `Call` and the coroutine entry points return a `ScriptResult` holding either a value or an error. An unhandled script error halts the instance, and the host simulation continues. | Specification in section 6. |
| DEC-13 | Fault granularity | Per mod; per instance | Per `WarScriptLanguage` instance | Follows from the topology in 1.3. What happens to sibling instances is host policy (Q-4). |
| DEC-14 | Compatibility | Keep compatibility; break freely | Break freely, migrate every script, no compatibility shims | Only the owner uses WarScript today. |
| DEC-15 | Priorities | Performance first; correctness and UX first | Correctness, security, determinism, language quirks, developer UX and tests first. Phase 4 performance work and multi-VM pooling or shared cores are deferred. | The owner considers performance mostly fine for the workload. Fixes that are also bugs, such as constructor execution (T1.1), stay in Phase 1 even though they improve performance as a side effect. |
| DEC-16 | Re-entrant host calls | VM pool; nested frames on the same VM; a fresh VM per nested entry | For now, the simplest correct approach: when the instance's VM is busy, a nested entry runs on a separate VM, and budgets are shared at the instance level (T1.5). Pools come later (F-VM-2). | The owner asked to defer pools and shared cores. |
| DEC-17 | Test investment | — | A headless test project, golden scripts, invariant checks, bytecode round-trips, a culture matrix, fuzzing and cross-platform determinism hashes (Phase 0, section 9) | The owner wants much better tests to catch more errors. |
| DEC-18 | Bytecode format hygiene | — | Explicit opcode numbers, a single opcode-info table, a version bump on every format change with exact-match loading, and a load-time verifier (T1.7) | Accepted along with the other review findings. |
| DEC-19 | Lexer token cache | Remove it; make it bounded and thread-safe | Bounded and synchronized (T1.9). Later superseded by shared compiled programs (F-VM-3). | Production loads bytecode from the archive, so the cache mainly helps the editor and hot reload. |
| DEC-20 | Verifying v2 against the game | (A) game-session audit reports and recorded game baselines, compared after the migration (v1.0 of this plan); (B) the language stands on its own: the headless test suite plus compile-time checks, no game-side gate | B (round 4) | The owner considers the game-side audits more than the language needs; the Phase 0 tests exist so the language can be verified without the game. The T0.8 owner step and T0.9 are dropped: nothing waits for game sessions, game baselines or reviews of them. What stays: the audit hooks and the test-suite audit report (they cost nothing and guide the migration of the test corpus), `Headless~/ModTrace` and `BASELINE.md` as optional tools. The game's scripts are still migrated (R4), by the static tool (8.2) and checked by the v2 compiler with the native manifest (PROP-30); anything that only fails at run time fails loudly, as a v2 error. |
| DEC-21 | Where checks happen | Runtime errors as specified in 5.3 to 5.9; as much as possible at compile time | Compile time wherever the information exists statically (round 4). A rule that is a runtime error in section 5 is also a compile error whenever the compiler can prove the failure. | The owner: "the more we can check at compile time, the better". Concretely: PROP-12 (native shadowing becomes an error), PROP-27 to PROP-31, and the widened T3.8. Runtime checks stay as the backstop for everything that depends on dynamic values. |

### 3.2 Proposed defaults

These details were not explicitly discussed but are needed for implementation. They are the approved defaults unless the owner says otherwise.

| ID | Topic | Default | Rationale |
|---|---|---|---|
| PROP-1 | Errors across host re-entry (script calls a native, the native calls `Call` on the same instance, the callback raises) | The nested `Call` returns an Error result to the native. The error then keeps propagating in the calling script from the native call site, where `begin/rescue` can catch it. Only an error that escapes the outermost entry halts the instance. | Normal exception semantics through callbacks, like Lua errors crossing C functions. Keeps `begin/rescue` meaningful around natives that fire events. |
| PROP-2 | Budgets with nesting | The instruction and memory budgets are reset only at the outermost entry: `Run`, `Call`, `StartCoroutine`, and each coroutine resume driven by `TickCoroutines`. Nested entries, constructors and every VM of the instance draw from the same budget. | Closes BUDGET-1 and its nested-call variant. |
| PROP-3 | Coroutine faults | A coroutine error halts the instance. `TickCoroutines` stops at the first fault and returns it; the remaining coroutines do not run that tick. | Same model as `Call`, and deterministic. |
| PROP-4 | Instance identity | A host-settable `InstanceLabel` string, included in every `ScriptError` and log line | Many instances can exist at once (1.3). |
| PROP-5 | Limits | `MaxTextLength` = 1,048,576 characters; `MaxArrayLength` = 1,048,576 elements; `MaxValueDepth` = 256; `MaxHostNesting` = 32; lambda-constant and class nesting in bytecode at most 32 levels. The text and array limits are configurable per instance. | Prevents out-of-memory conditions and native stack overflows, and the limits are deterministic. |
| PROP-6 | `array + x` | `array + array` produces a new concatenated array (unchanged). `array + x` and `x + array`, where `x` is neither an array nor text, raise TypeError; use `<<` to append. If either operand is text, the operation is text concatenation. | v1 silently copies and appends (SEM-11). DEC-8 did not state the array case explicitly. |
| PROP-7 | Replacement for `text - text` | A native `Text_remove [s, sub]` that removes all occurrences; an empty `sub` returns `s` unchanged | Follows the stdlib's `Array_*` naming. |
| PROP-8 | Instance identity under casts | `==` compares the root (most-derived) instance, so `d == (d as Base)` is true | In v1's inheritance model, casts return relation objects, which are different objects. |
| PROP-9 | Local-variable rules | A name assigned anywhere in a function, without `global`, is local to the whole function. Reading it textually before its first assignment is a compile error. At runtime, unassigned locals are null. Loop and rescue variables are scoped to their block, and may not shadow a parameter or local of the same function (compile error). | Simple and checkable at compile time, and avoids Python's UnboundLocalError surprise. |
| PROP-10 | `global` syntax | `global a, b` is a statement inside a function, method, lambda or class body, placed before any use of those names in that body. At top level it is a compile error. Assigning to a declared global creates it if it does not exist. In a class body, a name declared `global` is not a field. | Mirrors Python. Class bodies need it because a bare assignment there declares a field. |
| PROP-11 | Rescue variable | `rescue e` binds `e` for the rescue block only | Consistent with loop variables. |
| PROP-12 | Duplicate definitions | Two user definitions with the same function name and arity, or the same class name, anywhere in the module set are a compile error. A user function that shadows a native is also a compile error (DEC-21; v1.0 of this plan had a warning, as v1 logs today). | Static imports make this checkable. |
| PROP-13 | Exception classification | Exceptions thrown by native function bodies, including `NativeHelper` argument errors, become NativeError (rescuable) unless the native throws `ScriptErrorException` with a specific kind. Any other C# exception inside the VM becomes HostFault, which cannot be rescued, halts the instance, and is not assumed to be deterministic. | Natives are expected to be deterministic. VM-internal exceptions are bugs. |
| PROP-14 | Formatting native objects | Never call arbitrary host `ToString()`. `F64Vec3` formats as `(x, y, z)` using WarScript's integer formatter. Function values format as `<fun name>`. Other native objects format as `<TypeName>` unless the host registers a formatter. | DET-3 |
| PROP-15 | Writing an undeclared property | `obj :: newprop = v`, where `newprop` is not a declared field, raises NameError | Fields are declared by constructor parameters and class-body assignments (DEC-6). |
| PROP-16 | Lambdas | `this` or `@x` inside a lambda is a compile error. Reading a local of the enclosing function is a compile error with the message "closures are not supported". Otherwise lambdas follow function rules. | Lambdas have no closures today. This turns silent nulls into clear errors. |
| PROP-17 | Ordering booleans | `<`, `<=`, `>` and `>=` on logical values raise TypeError | Part of the strict operator table. |
| PROP-18 | Bytecode version check | The loader requires the exact current version and format hash | No compatibility (R4). |
| PROP-19 | Repository layout | Headless projects in `Headless~/`, one-off tools in `Tools~/`, documentation in `Documentation~/`, golden scripts in `Tests/Golden~/` | Unity ignores folders ending in `~`, so there is no `.meta` churn and no accidental import of `bin/` or `obj/` output. |
| PROP-20 | Globals in methods and class bodies | In v2, bare reads of globals work everywhere: functions, methods, class bodies and lambdas. In v1 they returned null inside methods and class bodies (SEM-4). | Consistency |
| PROP-21 | `yield` in class bodies | Compile error | Constructors run as frames (T1.1), and a suspended, half-constructed object is not useful. |
| PROP-22 | The value bound by `rescue e` for built-in errors | Remains the message text, as in v1. Values raised with `raise` pass through unchanged. Structured error objects are future work (F-LANG-2). | Limits scope. |
| PROP-23 | `and` and `or` | Unchanged: they short-circuit and return the deciding operand, so `0 or 5` is `5` and `3 and 7` is `7` | Verified v1 behavior, now documented. |
| PROP-24 | Coroutine resume order | Unchanged: `TickCoroutines` resumes coroutines newest first (reverse start order). `StartCoroutine` runs the new coroutine immediately until its first yield. | Deterministic, now documented. Q-8 asks the owner to confirm the order is intended. |
| PROP-25 | `Call` before `Run` | Allowed. The script compiles automatically, but top-level code does not run, so its globals are not initialized. Same as v1, minus the tree-walk path. | Preserves existing usage. Q-9 asks the owner to confirm. |
| PROP-26 | Unused locals | Compile warning: "local 'x' is assigned but never read" | Under DEC-5, a typo on the left side of an assignment silently creates a new local. This warning catches it. |
| PROP-27 | Undefined globals | Reading a name that is not a local, not assigned anywhere in the module set (at top level or through `global`) and not a known host global is a compile **error**, `undefined variable 'x'`, not a warning. Known host globals are those in the host scope when `Compile()` runs plus those listed in the native manifest (PROP-30). | DEC-21. Under DEC-5 such a read can only raise NameError at runtime, so reporting it early loses nothing. |
| PROP-28 | Statically typed operands | The compiler knows an operand's type when it is a literal, an interpolated text, an array literal, `null`, `true`/`false`, a `const` or enum member with such a value, or an operator result whose type follows from 5.3 (`text + x` is text, `n * n` is number, and so on). Any operator, index or property rule of 5.3 to 5.5 that would raise TypeError for those types is a compile error with the same message, for example `"Sir " + t - "Sir "`, `-"x"`, `true < 1`, `{1} + 2`, `5 :: name`. No inference through variables or calls. | DEC-21. Cheap and never wrong. It turns the common form of text subtraction (a literal or interpolated operand) into a compile error, so the migration does not depend on finding it at runtime. |
| PROP-29 | Statically checkable names | Compile errors for: `new C [...]` with an arity that matches no constructor of `C`; a named argument naming no parameter of the called function, or given twice; duplicate parameter names; duplicate enum members; `E :: X` where `E` is a known enum and `X` is not a member (or `values`, `names`, `count`, `name`); `obj :: x` where `x` is neither a field nor a method of any class in the module set or the host-registered classes; `this`/`@x` outside a class; `break`/`next` outside a loop. Warning: code after `return`, `break`, `next` or `raise` in the same block is unreachable. | DEC-21. Each uses only information the compiler already holds after static imports (T3.7). |
| PROP-30 | Native manifest | The binding generator always emits a native signature manifest (each native's name and arities, plus `[WsConst]`/`[WsEnum]` names and host classes), and the compiler accepts one in place of registered natives. Headless tools (the runner, the migration tool, a later `wsc`) compile game scripts against it. | DEC-20 and DEC-21: the game's scripts can then be checked without running the game, which answers Q-2 regardless of how the archive build is set up. |
| PROP-31 | Warnings as errors | `Compile()` takes an option that turns warnings into errors. Off by default; the mod archive build should turn it on. | Lets the shipped scripts be held to the strictest checks without making editing in the editor noisy. |

---

## 4. Review findings

All findings below were verified by running the code (section 1.4) unless marked otherwise. Repro scripts and expected v2 outcomes are collected in Appendix A.

### 4.1 Crashes and budget bypass

| ID | Finding | Evidence (v1) | Root cause | Fixed by |
|---|---|---|---|---|
| CRASH-1 | Cyclic arrays crash the process. | `a = {1}`, `a << a`, `print a` aborts the process with "Stack overflow" in `WarValue.ToString()`. `a == b` with two cyclic arrays aborts the same way in `WarValue.Equals`. | `WarValue.ToString`, `Equals` and `GetHashCode` recurse without limit, and `ClassData.StructuralEquals` and `StructuralHashCode` have the same shape. A .NET stack overflow cannot be caught. | T1.2 (fixed) |
| CRASH-2 | A recursive constructor crashes the process, even with `InstructionBudget` set. | `class Node [v]` whose body does `child = new Node [v + 1]`, then `new Node [0]`, aborts with a stack overflow after 571 nested `ExecuteLoop`/`Execute` levels. | `WarVM.InstantiateClassDefShared` runs each constructor with `new WarVM(_script).Run(...)`, which recurses on the C# stack. `FramesMax` only applies within one VM. | T1.1 (fixed) |
| CRASH-3 | C# exceptions escape the VM. | `x = "abc" - ""` throws `ArgumentException` (from `String.Replace` with an empty `oldValue`) out of `Run()`, and `begin/rescue` cannot catch it. `x = "ab" * 2000000000` throws `ArgumentOutOfRangeException` because the length computed in `RepeatString` overflows `int`. | `Execute()` only catches `IndexOutOfRangeException` when `_sp > StackMax`. There are no size or argument pre-checks. The native path of `CallMethod` has no try/catch, although `Call` has one. An escaping exception leaves `MemoryContext`, `DefinitionContext` and `ClassInstanceContext` half-pushed. | T1.3, T1.4 (fixed) |
| CRASH-4 | Re-entrant host calls corrupt the VM. | A native `fire []` that calls `script.Call(on_event, 42)` on the same instance: the outer script prints `after fire, a=1 b=7`, then an `IndexOutOfRangeException` escapes to the host. | Host calls reuse the instance's cached `WarVM`. `RunFunction` calls `Reset()` while the outer execution is still running on that VM. | T1.5 (fixed) |
| CRASH-5 | A class that inherits from itself crashes the process at `new`. Found by the fuzzer (T0.7). | `class A` / `end` / `class A : A` / `end` / `a = new A` aborts with a stack overflow after about 10,000 nested `InstantiateClassDefShared` calls. Defining the classes is accepted; a constructor body is not needed. | `InstantiateClassDefShared` follows `BaseTypes` by name and recurses on the C# stack with no cycle check. The redefinition replaces `A`, so its base resolves to itself. | T1.1 (fixed, Q-11 default) |
| CRASH-6 | Malformed source makes the parser and compiler throw C# exceptions other than `SyntaxException`. Found by the fuzzer (T0.7). | `x = (1]` throws `System.Exception` "Operator LeftParen is not a binary operator" from `OperatorExtension.ToBinaryExpression`. `x = p as "R" :: n` throws `InvalidCastException` from `Compiler.CompileExpression`. Both escape `Run()` instead of being reported as syntax errors. | Unchecked assumptions about expression shapes in the expression reader and compiler | T1.8 (fixed) |
| CRASH-7 | Leaving an `ensure` block by `raise` or `return` while another error is propagating corrupts the exception state. Found by the fuzzer (T0.7). | `begin` / `begin` / `raise "first"` / `ensure` / `raise "second"` / `end` / `rescue e` / `print e` / `end`: the outer `rescue` does not run and `Run()` throws `NullReferenceException` from `ExceptionContext.PrintStackTrace`. Without the outer `begin/rescue`, v1 reports "second" as unhandled. A function whose `ensure` does `return 1` after `raise "first"` returns 1 but leaves a pending exception and a handler on the VM; called inside `begin/rescue`, "first" later escapes unhandled. | The `ensure` path re-enables the saved exception state without checking how the block was left, so the state ends up `Raised` with a null `Exception`, or a discarded error is revived. | T1.3 (fixed) |
| CRASH-8 | `break` or `next` inside a `begin` block leaves the block's exception handler on the VM. Found by the fuzzer during T1.2. | `loop true` / `if true` / `begin` / `break` / `rescue e` / `print "stale rescue"` / `end` / `end` / `end` / `assert false`: the failed assertion is caught by the stale handler, the rescue body inside the loop runs again with the wrong scope depth, and the run ends in a host fault (`InvalidOperationException` "Stack empty" from `MemoryContext.EndScope`). Without the `if`, the rescue body and the loop repeat until the instruction budget runs out. | `Compiler.CompileBreak` and `CompileNext` pop the scopes and locals opened since the loop started, but not the exception handlers (`PushHandler`) of `begin` blocks opened since then; an `ensure` block is skipped as well. | T1.3 follow-up (fixed, Q-12 default) |
| BUDGET-1 | Instruction and memory budgets can be bypassed. | With `InstructionBudget = 5000`, a loop creating 200 instances whose constructor loops 300 times completes and prints `done 200`. The same work written inline fails with "Instruction budget exceeded". | Each constructor, and each import, runs on a new `WarVM` whose `Reset()` reloads full budgets. | T1.1 (constructors), T1.5 (imports and nested entries; fixed) |
| BUDGET-2 | Rescuing "Instruction budget exceeded" disarms the budget, so a script can run forever. Found by the fuzzer (T0.7). | With a budget of 1,000, `begin` / `loop true` / `end` / `rescue e` / `end` followed by a 100,000-iteration loop completes. With an infinite loop instead, `Run()` never returns. | `WarVM` raises when `--_budget` reaches 0, and a budget of 0 means unlimited. The error is rescuable like any other. | T1.5 (fixed, Q-11 default) |
| BUDGET-3 | An array that holds the same array several times makes `==`, `!=`, the array natives and formatting run for an exponential time that no budget interrupts. Found reviewing T1.2. | `a = {1}`, then `a = {a, a}` 40 times (about 200 instructions), then `print a` or `a == b` (with `b` built the same way): with `--budget 100000`, neither returns. | `WarValue.Equals` and the array formatter follow every path through shared arrays, and there are 2^40 paths. A single `Equal` instruction or `print` is charged once. `StateHash` walks the same way. | T1.2 review (fixed for equality and formatting); state hash open (Q-13) |

### 4.2 Determinism

| ID | Finding | Evidence (v1) | Root cause | Fixed by |
|---|---|---|---|---|
| DET-1 | Negative integers format differently depending on the OS locale. | Formatting `0 - 1500` under .NET 8 with ICU: en-US and de-DE produce `-1500` (U+002D); sv-SE, nb-NO and fi-FI produce `−1500` (U+2212). | `ToInt(Numeric).ToString()` in `WarValue.ToString` uses the current culture. Unity's Mono culture data was not tested, but the code is culture-dependent either way. | T1.6 (fixed) |
| DET-2 | Fallback number formatting goes through `double`. | Fractional values with no round-trippable form of 9 or fewer digits print via `F64.ToString()`, which is `Fixed64.ToString`, which is `ToDouble(v).ToString(InvariantCulture)`. `FormatFractional` also uses a try/catch for each candidate. | This is the only `double` in the runtime, and `double` formatting differs between Mono's class libraries and CoreCLR. | T1.6 (fixed) |
| DET-3 | Native objects format via the host's `ToString()`. | `F64Vec3(1, 1/3, -2)` prints as `(1, 0.3333333332557231, -2)`, because FixPointCS formats each component through `double`. Every `NativeObject` uses `Ref.ToString()`. | The `NativeObject` branch of `WarValue.ToString`. | T1.6 (fixed) |
| DET-4 | Stale bytecode loads silently. | The switch from `double` to `F64` kept format version 1. The loader rejects only newer versions. `OpCode` values are implicit, so inserting an opcode renumbers every later one. | `BytecodeSerializer.Load`, `OpCode.cs` | T1.7 (fixed) |

### 4.3 Bytecode loading

| ID | Finding | Evidence | Root cause | Fixed by |
|---|---|---|---|---|
| SEC-1 | Loaded bytecode is not verified. | Unknown opcode bytes fall through the dispatch `switch`, which has no `default` case, and execution continues misaligned. Jump targets, constant, cache and local indices, and exception-handler targets are not checked. A `SetLocal` with a large slot writes into another frame's region or throws an `IndexOutOfRangeException` that escapes the VM. Lambda constants are read recursively, so crafted nesting can overflow the loader's stack. Truncated input throws `EndOfStreamException`. (Found by code reading.) | `BytecodeSerializer`, `WarVM` | T1.7 (fixed) |
| SEC-2 | Detecting stack overflow depends on bounds-check exceptions. | Value-stack overflow is detected by catching `IndexOutOfRangeException`. If IL2CPP array bounds checks are disabled for performance, this becomes memory corruption. | `WarVM.Execute` | T1.7 (fixed: `MaxStack` checked at every frame push), T3.5 (locals) |

### 4.4 Language semantics

| ID | Finding | Evidence (v1) | Root cause | Fixed by |
|---|---|---|---|---|
| SEM-1 | Functions overwrite globals implicitly. | With `count = 10` at top level, a function that does `count = 0` and then loops leaves the global `count` at 3. | Assigning to a non-local compiles to `SetGlobal`. `MemoryScope.Set` walks the parent chain and writes the first match it finds. | DEC-5 |
| SEM-2 | Typos and undefined names are silent. | `helth = health - amt` creates a new variable, and reading an undefined name returns null. | `MemoryScope.Set` and `MemoryScope.Get` | DEC-5, PROP-26 |
| SEM-3 | Locals in function bodies are dictionary lookups. | In a function, `t = 0` and `t = t + 1` compile to `SetGlobal`/`GetGlobal` by name, and every call pushes a pooled `MemoryScope`. | The compiler gives stack slots only to parameters and loop variables. | DEC-5, T3.4, T3.5 |
| SEM-4 | Methods and class bodies cannot see globals. | With `g = 5`, a method doing `return g` returns null, and a class body doing `seen = g` sets `seen` to null. | A class instance's `MemoryScope` is created with a null parent. | PROP-20 |
| SEM-5 | Block scoping is inconsistent. | At top level, `if true` / `made = 1` / `end` / `print made` prints null. The same code inside a function prints 1. A loop variable read after its loop is null. | Top-level blocks emit `PushScope`/`PopScope`; function blocks do not. | DEC-4 |
| SEM-6 | Values are coerced implicitly. | `1 == "1"` is True. `"10" < 9` is True, and `10 < "9"` is also True. `-"abc"` is 0. `true + 1` is `"True1"`. `5 - "5"` is `""`. | Mixed-type paths fall back to comparing or concatenating `ToString()` results, and `Negate` does not check types. | DEC-8 |
| SEM-7 | Errors pass silently. | `arr{10} = 99` on a 3-element array is ignored. `arr{10}` and `arr{-1}` return null. `x :: hp = 5` with `x = null` raises nothing, although reading `x :: hp` does raise. `5 << 3` evaluates to 5 with no error. | Missing `else` branches in `IndexSet*`, `SetProperty` and `ArrayAppend` | DEC-10 |
| SEM-8 | Class equality is structural. | `(new P [1]) == (new P [1])` is True. | `ClassData.StructuralEquals` | DEC-9 |
| SEM-9 | Booleans print capitalized. | `print true` prints `True`. | `WarValue.ToString` | DEC-11 |
| SEM-10 | Unused opcodes have semantics that differ from the ones actually used. | `EqualJump` and `NotEqualJump` compare with type-strict `Equals`, while `Equal` coerces through `ToString`. Only `LessJump` is ever emitted by the compiler. `YieldUntil` is reserved and unused. | `OpCode.cs`, `WarVM` | T1.7 (fixed) |
| SEM-11 | `array + value` appends to a copy. | `{1, 2} + 5` evaluates to `[1, 2, 5]`. | `WarVM.AddArrays` | PROP-6 |
| SEM-12 | A second `break` or `next` in the same top-level loop pops too few block scopes, so later assignments create variables in a leaked scope instead of globals. Found while fixing CRASH-8. | `fun read_y [] return y end`, a `loop i in 0..10` with `if i == 50` / `break` / `end` then `if i == 2` / `break` / `end`, then `y = 5` and `print read_y []` prints `null`. The leaked scope is popped when the script frame returns, so the invariants do not see it. | `Compiler.CompileBreak`/`CompileNext` emitted their `PopScope`s through `EmitPopScope`, which also lowers the compiler's runtime scope depth for the code after the jump, so the next `if` block's `break` counted one scope too few. | T1.3 follow-up (fixed with CRASH-8) |
| SEM-13 | Leaving a rescue body by `return`, `raise` or a runtime error skips the `ensure` block. Found reviewing the CRASH-8 fix. | `begin` / `raise "a"` / `rescue e` / `return 1` / `ensure` / `print "ensure"` / `end` inside a function returns 1 without printing; `raise "b"` or `1 / 0` in the rescue body propagates without running the ensure block either. Falling off the end of the rescue body, and since the CRASH-8 fix `break`/`next` out of it, do run it. | `WarVM.DoHandleException` removes the block's handler before jumping to the rescue body, so `Return` and the next `DoHandleException` find no handler with that ensure block. | T1.7 (fixed, Q-14) |
| SEM-14 | A `yield` outside a coroutine in a called function stops `Run()` silently and leaves the callee's contexts pushed. Found by the bytecode fuzzer (T1.7), where a `Return` turned into `YieldWait`; reproducible from source. | `fun f []` / `yield wait 1` / `return 1` / `end` then `print f []` and `print "after"` at top level: nothing is printed, no error is raised, and `MemoryContext` is one scope deeper after `Run()` (two for a method). T1.1 recorded the method and constructor variants as unchanged v1 behaviour. | `Run()` treats any yield as the end of the entry; only a `yield` in the script body leaves nothing pushed (the invariants allow that case). | Open (Q-15) |

### 4.5 Host API and developer experience

| ID | Finding | Evidence (v1) | Fixed by |
|---|---|---|---|
| API-1 | The host cannot get return values. | `Call` returns void, and `CallBytecode` discards the result of `RunFunction`. | T2.3 |
| API-2 | The host cannot see errors. | After a `Call` that divides by zero, `ExceptionContext.IsRaised()` is false: the error was sent to `Logger` and then cleared. | T2.3, T2.4 |
| API-3 | There are two execution engines. | A tree-walk fallback for functions that are not compiled yet, plus the tree-walk `Coroutine.cs`. | T2.1 (fixed) |
| API-4 | The host API allocates per call, and handles go stale. | `Call(..., params WarValue[])` allocates an array per call. `FunctionDefinition` handles go stale after `Reload`. | T2.6, T2.7 |
| DX-1 | The lexer cache is static, unbounded and unsynchronized. | It is keyed by the full source string and never evicted (the TODO comments in `LexicalParser` say so), and it is shared by all instances and threads. | T1.9 (fixed) |
| DX-2 | The compiler truncates silently. | Argument counts and `PopN` operands are cast to `byte`. Constant indices, jump offsets, handler addresses and array-literal counts are 16-bit with no overflow check, so an overflow produces wrong jumps instead of an error. | T1.8 (fixed) |
| DX-3 | Line information is missing in places. | Several emit sites for calls, `new` and nested `new` pass line 0, which shows up in disassembly (for example a loop-body `GetLocal` at line 0). `Lines` stores one `int` per code byte. | T1.10 (fixed), T1.7 (line table fixed: run-length) |
| DX-4 | Diagnostics are weak. | Errors carry a row but no column, parsing stops at the first error, and messages read like "After `X` declaration expected any of the following lexemes ...". | T3.1, T3.8 |
| DX-5 | The disassembler decodes some instructions incorrectly. | It treats `PushHandler` as 7 bytes long, but it is 8. `IndexSetLocal`, `IndexSetGlobal` and `NewNestedInstance` fall through to the 1-byte default case. Disassembling a function containing `begin/rescue` and `arr{i} = 1` threw `ArgumentOutOfRangeException`. | T1.7 (fixed) |
| DX-6 | The runtime has static mutable state. | The lexer cache is the only mutable static found. `OperatorExtension.OperatorMap`, `StringInterner.IntegerStrings` and `WarScriptLibraryRegistry.Libraries` are initialized once and then only read. | T1.9 (fixed) |

### 4.6 Performance

Performance is not a priority (DEC-15). These findings are recorded so that Phase 4 starts from data. The numbers are in Appendix B.

| ID | Finding | Fixed by |
|---|---|---|
| PERF-1 | Creating an instance whose class has a constructor body allocates about 29 KB, because each constructor gets a new `WarVM` with a 1,024-slot stack. 20,000 instances allocated 565 MB. | T1.1 (fixed: about 720 bytes per instance) |
| PERF-2 | Every native call allocates a `List<WarValue>` for its arguments (80 B per call; 15.3 MB for 200,000 calls). | F-PERF-2 |
| PERF-3 | Every call resolves the callee by hashing a `(string, int)` key up the definition-scope chain, and pushes a `MemoryScope`. Method calls also walk the inheritance chain and push three context stacks. | T3.5 (scope pushes), F-PERF-1 (call-site caches) |
| PERF-4 | String interpolation produces one intermediate string per `+`, and each one is hashed into the interner. | F-PERF-3 |
| PERF-5 | Dispatch costs about 10 ns per instruction under RyuJIT, and a simple counted loop executes 14 dispatches per iteration. | F-PERF-4, F-PERF-5 |
| PERF-6 | A trivial instance costs about 178 KB, most of it a string-interner dictionary pre-sized to 4,096 entries. | F-VM-1 |

---

## 5. Language v2 specification

This section is the normative target for the language after Phase 3. Anything not mentioned here keeps its v1 behavior. In particular, these are unchanged:

- the syntax of `fun name [args] ... end`;
- `[ ]` for parameter lists and calls, `{ }` for array literals and indexing, and `::` for property access;
- the `loop` forms, `begin/rescue/ensure`, the `yield` forms, `const`, `enum`;
- string interpolation, escapes and raw strings;
- the numeric-literal grammar and CLAUDE.md D1 to D7.

When T3.10 is done, this section becomes `Documentation~/LANGUAGE.md`, and every row of the operator table gets a golden test.

### 5.1 Values

| Type | Description |
|---|---|
| null | The absence of a value. |
| number | An `F64`, signed 32.32 fixed point. Literals follow CLAUDE.md. Conversion to an integer truncates toward zero (D1). Overflow behaves as FixPointCS does (wrapping for `+` and `-`); this is unchanged and listed as F-LANG-3. |
| logical | `true` or `false` |
| text | An immutable UTF-16 string. Compared ordinally. |
| array | Mutable, zero-based, with reference semantics. |
| instance | A class instance, with reference semantics. |
| native | An opaque host reference: `F64Vec3`, function values (lambdas and function references) and host objects. |

### 5.2 Truthiness

`null`, `false` and the number `0` are falsy. Everything else is truthy, including `""`, empty arrays and instances. This is unchanged from v1.

### 5.3 Operators

| Expression | v1 behavior | v2 behavior |
|---|---|---|
| `n + n` | Addition | Addition |
| `text + x` or `x + text` | Concatenation via `ToString()` | Concatenation using the canonical formatting in 5.10 |
| `array + array` | New concatenated array | Unchanged |
| `array + x` or `x + array`, where `x` is neither array nor text | Copy of the array with `x` appended or prepended | TypeError (PROP-6) |
| `n - n` | Subtraction | Subtraction |
| `text - text` | Removes occurrences of the right operand | Removed; raises TypeError. Use `Text_remove [s, sub]` (PROP-7). |
| `-` with mixed types | `ToString` and replace, so `5 - "5"` is `""` | TypeError |
| `n * n` | Multiplication | Multiplication |
| `text * n` or `n * text` | Repeat; a count of 0 or less gives `""` | Repeat. The count truncates toward zero, and a count of 0 or less gives `""`. A result longer than `MaxTextLength` raises LimitExceeded. |
| `n / n` and `n % n` | Division and modulo; division by zero raises an error | Unchanged. Division by zero raises DivisionByZero; non-numeric operands raise TypeError. |
| `-x` | No type check, so `-"abc"` is 0 | Numbers only; anything else raises TypeError |
| `!x` | Logical not of truthiness | Unchanged |
| `a and b`, `a or b` | Short-circuit, returning the deciding operand | Unchanged (PROP-23) |
| `<`, `<=`, `>`, `>=` | Numbers and text; mixed types compare `ToString()` results ordinally, so `"10" < 9` is true | Number with number, or text with text (ordinal). Anything else raises TypeError, including logical values (PROP-17). |
| `==`, `!=` | Special rules for null; same-type values use `Equals`; mixed types compare `ToString()` results, so `1 == "1"` is true | Same-type values follow 5.4. Values of different types are not equal and raise no error. |
| `array << x` | Appends `x` and evaluates to the array | Unchanged. Raises LimitExceeded at `MaxArrayLength`. |
| `x << y`, where `x` is not an array | Silently does nothing | TypeError |
| `obj is C`, `obj as C` | | Unchanged |

`+` checks its operands in this order: if either operand is text, it concatenates; otherwise, if both are numbers, it adds; otherwise, if both are arrays, it concatenates them; otherwise it raises TypeError.

TypeError messages follow the form `cannot apply '<op>' to <type> and <type>`, for example `cannot apply '-' to text and number`. The type names are `null`, `number`, `logical`, `text`, `array`, `instance of <Class>` and `native <TypeName>`.

### 5.4 Equality

`null == null` is true, and null is not equal to any other value. Numbers compare by their raw `F64` bits. Logical values compare by value, and text compares ordinally.

Arrays compare structurally, element by element. If the comparison nests deeper than `MaxValueDepth`, it raises LimitExceeded; this is how cyclic arrays are handled.

Class instances compare by identity of their root, most-derived instance (PROP-8).

Native objects compare with `Equals`. Function values are equal when they refer to the same compiled function, and `F64Vec3` compares by value, because it is a struct.

`!=` is always the negation of `==`.

### 5.5 Indexing, text and properties

**Array indexing.** `arr{i}` requires a numeric `i`, which truncates toward zero (D1). Any other index type raises TypeError, and an index with `i < 0` or `i >= length` raises IndexError. `arr{i} = v` follows the same rules; arrays never grow automatically, so use `<<` to append.

**Text.** `text{i}` returns a one-character text, with the same bounds rules. `text{i} = v` raises TypeError, because text is immutable (DEC-10). Indexing any other type raises TypeError.

**Iteration.** Iterating over an instance with `loop v in obj` and taking its length are unchanged: they use the property values and the property count.

**Properties.**

- `obj :: name` on anything that is not an instance raises TypeError, and naming a property that is not declared raises NameError.
- `obj :: name = v` follows the same two rules: TypeError on a non-instance, and NameError for a property that is not declared (PROP-15).
- Declared properties are the constructor parameters plus the fields declared in the class body, including those of base classes (5.7).

### 5.6 Variables and scope

**Top level.** Assignments at the top level of a file create or update globals of the instance. All files imported into one instance share a single global namespace. Host-provided globals (`[WsConst]` and `[WsEnum]` values in `GlobalMemoryScope`) are visible to every file. Constants cannot be reassigned, and this is checked at compile time across the whole module set.

**Functions, methods and lambdas.**

- Parameters are locals.
- A name assigned anywhere in the body, and not declared with `global`, is a local of the whole function (PROP-9).
- Locals are stored in stack slots, and unassigned locals read as null.
- Reading a local textually before its first assignment is a compile error.

**Loop and rescue variables.** A `loop` variable and a `rescue` variable exist only inside their block. Shadowing a parameter or local of the same function with one is a compile error.

**`global`.** `global a, b` declares that assignments to `a` and `b` in this body write the instance globals, creating them if needed. It is allowed in function, method, lambda and class bodies. The declaration must come before any use of those names in the body, and it is a compile error at top level (PROP-10).

**Name resolution for reads.** For a bare name, resolution checks, in order: a loop or rescue variable in scope; a parameter or local; a global, looked up at runtime. If no global exists at runtime, the read raises NameError with the message `undefined variable 'x'`.

**Name resolution for calls.** In `f [args]`, if `f` is a local or parameter holding a function value, the call goes through that value. Otherwise, if a function named `f` with a matching arity exists among the module's definitions or the natives, that function is called. Otherwise, if a global holding a function value exists at runtime, it is called.

**Compile-time checks** (T3.8, DEC-21). Whatever the compiler can prove is reported before the script runs; the runtime errors in this section and in 5.3 to 5.9 remain as the backstop for dynamic values.

- A call to a name that is not a known function, not a native and not assigned anywhere as a variable is an error.
- A call with an arity that matches none of a known function's arities is an error.
- A read of a name that is not a local, is never assigned anywhere in the module set and is not a known host global is an error, `undefined variable 'x'` (PROP-27).
- Operator, index and property type errors on operands whose type is known statically are errors (PROP-28).
- The name checks of PROP-29 (constructor arity, named arguments, duplicate parameters and enum members, unknown enum members, property names unknown to every class, `this` outside a class, `break`/`next` outside a loop) are errors.

**Compile-time warnings** (errors when the host compiles with warnings as errors, PROP-31):

- A local that is assigned but never read (PROP-26).
- Unreachable code after `return`, `break`, `next` or `raise` (PROP-29).

Example:

```ruby
count = 0                 # global

fun bump [n]
    global count          # required to write the global
    total = n * 2         # local to bump
    count = count + total
    return total
end

fun peek []
    return count          # reading a global needs no declaration
end
```

### 5.7 Classes and fields

The declaration syntax is unchanged: `class Name [params] : Base [args] ... end`, including multiple inheritance and nested classes.

**Fields** are the constructor parameters plus every name assigned with a bare assignment directly in the class body. The compiler collects the field list at compile time, and it includes base-class fields resolved through static imports. Each field gets a fixed index.

**The class body** runs as the constructor, after all base-class constructors have run, in declaration order (unchanged from v1). Inside the class body:

- a bare name refers to a field of this class or its bases, falling back to a global (PROP-20);
- a bare assignment declares a field, unless the name is declared with `global` at the start of the class body (PROP-10);
- `@x` and `this :: x` may also be used;
- `yield` is a compile error (PROP-21).

**Methods:**

- Bare names follow the function rules in 5.6.
- `@x` reads or writes field `x` of `this`. It is equivalent to `this :: x` and is checked at compile time against the fields of the class and its bases, so an unknown field is a compile error.
- `obj :: x` on other instances is checked at runtime and raises NameError for unknown properties. If `x` is not a field or method of any class in the module set or the host-registered classes, it is already a compile error (PROP-29).

**Lambdas** cannot use `this` or `@x` (PROP-16).

**Equality, casts and nested classes.** Instance equality uses identity of the root instance (5.4). `as` and `is` are unchanged, and so are nested classes and `new Outer :: Inner [...]`-style instantiation as supported by v1.

**Lexing `@x`.** `@x` is desugared at lex time into the tokens for `this :: x`. The tokens carry the original line and column, so error messages point at the `@`.

### 5.8 Imports and module initialization

`import "path"` statements must appear before any other statement in a file; comments may come first. An import anywhere else is a compile error.

**Resolution and compile-time checks.** Imports are resolved when the module set is compiled or loaded, through `BytecodeResolver` and then `FileResolver`. An import cycle is a compile error that reports the cycle's path.

**Initialization order.** Each file's top-level code runs exactly once per instance, with dependencies first. The order is a depth-first post-order over the imports, taken in textual order, so it is deterministic.

**One definition namespace.** All functions and classes in the module set form one namespace per instance. Duplicate definitions are an error (PROP-12).

**Bytecode.** When loading bytecode, the module header lists the import paths (format v3, section 10.2), so the loader can resolve them without parsing source. The `Import` opcode is removed.

### 5.9 Errors

Every runtime error has a kind:

| Kind | Raised for |
|---|---|
| Raised | A `raise value` statement. The value may be of any type; `raise null` raises the text `"Empty exception"`, as in v1. |
| TypeError | Operator or operand type mismatches (5.3), calling a non-callable value, property access on a non-instance |
| IndexError | An index out of bounds (5.5) |
| NameError | An undefined variable at runtime, an unknown property, or a function missing at runtime |
| ArgumentError | Wrong arity at runtime, or invalid native arguments raised by `NativeHelper` |
| DivisionByZero | `x / 0` and `x % 0` (CLAUDE.md D4) |
| AssertionFailed | A failing `assert` |
| NativeError | Any other exception thrown by a native function body (PROP-13) |
| InstructionBudgetExceeded | The instruction budget is used up (PROP-2) |
| MemoryBudgetExceeded | The memory budget is used up (PROP-2) |
| LimitExceeded | The value stack, frames, exception handlers, host nesting, text length, array length or value depth exceeded a limit (5.12) |

**Rescuing errors.**

- `begin ... rescue e ... ensure ... end` rescues every kind listed above.
- `e` is the raised value for Raised errors, and the message text for every other kind (PROP-22).
- `ensure` semantics are unchanged.

**Errors across native calls.** When an error propagates out of a native call that re-entered the same instance, it continues in the calling script from the native call site (PROP-1).

**HostFault** (section 6) is not a script error kind. It cannot be rescued.

**Unhandled errors.** An error that escapes the outermost entry halts the instance (6.4).

### 5.10 Canonical formatting

These rules are used by `print`, text concatenation and interpolation:

| Value | Formatting |
|---|---|
| null | `null` |
| logical | `true` or `false` (DEC-11) |
| integer number | Invariant digits with an ASCII `-` sign |
| fractional number | The shortest decimal of up to 9 fractional digits that parses back to the same raw value through the integer-only literal parser. If there is none, the exact decimal expansion computed with integer arithmetic, which terminates within 32 fractional digits; for example 1/3 prints as `0.33333333325572311878204345703125`. There are no exceptions and no `double`. |
| text | The text itself |
| array | `[a, b, c]`, formatting the elements recursively. An array already being formatted prints as `[...]`, and so does any element beyond `MaxValueDepth`. |
| instance | The class name |
| function value | `<fun name>` |
| `F64Vec3` | `(x, y, z)`, using the number rules above |
| other native object | `<TypeName>`, or the output of a formatter registered by the host (PROP-14) |

### 5.11 Coroutines

`StartCoroutine` runs the coroutine immediately until its first yield. `TickCoroutines(dt)` then resumes ready coroutines newest first (PROP-24).

The yield forms are unchanged: `yield` waits for the next tick; `yield wait <seconds>` waits until the accumulated `dt` reaches the duration; `yield until <cond>` re-checks the condition each tick. Looping coroutines restart when they complete.

Budgets reset at each resume, because a resume is an outermost entry. A coroutine error halts the instance (PROP-3).

### 5.12 Limits

| Limit | Value | Error kind |
|---|---|---|
| Value stack | 1,024 slots per VM. Checked at call time against the function's `MaxStack` after T1.7 and T3.5. | LimitExceeded ("Value stack overflow") |
| Call frames | 128 per VM, constructor frames included | LimitExceeded ("Stack overflow") |
| Exception handlers | 32 | LimitExceeded |
| Arguments per call | 255 | Compile error |
| Constants per function | 65,535 | Compile error |
| Bytecode per function | 65,535 bytes (16-bit jumps) | Compile error |
| Elements in an array literal | 65,535 | Compile error |
| `MaxTextLength` | 1,048,576 characters (configurable) | LimitExceeded |
| `MaxArrayLength` | 1,048,576 elements (configurable) | LimitExceeded |
| `MaxValueDepth` | 256 | LimitExceeded for equality; `[...]` when formatting |
| `MaxHostNesting` | 32 nested host entries per instance | LimitExceeded |
| Lambda and class nesting in a bytecode file | 32 | Loader rejects the file |

---

## 6. Host API v2 specification

Implemented in Phase 2 (T2.2 to T2.8), on top of the Phase 1 fixes.

### 6.1 Types

The shapes below are normative. Exact member names may be adjusted if the codebase's conventions suggest better ones; record any deviation in this section.

```csharp
namespace WarScript
{
    public enum ScriptStatus : byte { Ok, Error, Halted, HostFault }

    public enum ScriptErrorKind : byte
    {
        Raised, TypeError, IndexError, NameError, ArgumentError, DivisionByZero,
        AssertionFailed, NativeError, InstructionBudgetExceeded, MemoryBudgetExceeded,
        LimitExceeded, CompileError, HostFault
    }

    public readonly struct ScriptFrame
    {
        public readonly string Function;    // "<script>", "tick", "Unit.hit", "Unit#ctor", "<lambda>"
        public readonly string ScriptName;
        public readonly int Line;
        public readonly int Column;         // 0 until T3.1
    }

    public sealed class ScriptError
    {
        public ScriptErrorKind Kind { get; }
        public string Message { get; }
        public WarValue Value { get; }                          // raised value; message text for built-in kinds
        public string ScriptName { get; }
        public string? InstanceLabel { get; }
        public int Line { get; }
        public int Column { get; }
        public IReadOnlyList<ScriptFrame> CallStack { get; }    // innermost first
        public System.Exception? HostException { get; }         // HostFault only
        public override string ToString();                      // "file:line:col: Kind: message" + call stack
    }

    public readonly struct ScriptResult
    {
        public ScriptStatus Status { get; }
        public WarValue Value { get; }        // Ok: the return value (Run: the top-level return value or null)
        public ScriptError? Error { get; }    // Error/HostFault: the error. Halted: the fault that halted the instance.
        public bool IsOk => Status == ScriptStatus.Ok;
    }

    public sealed class FunctionHandle { /* name, arity, owning instance; re-resolves after Reload (T2.6) */ }

    // Natives throw this to raise a specific script error kind (6.9).
    public sealed class ScriptErrorException : System.Exception
    {
        public ScriptErrorException(ScriptErrorKind kind, string message);
        public ScriptErrorKind Kind { get; }
    }
}
```

The additions to `WarScriptLanguage`:

```csharp
public string? InstanceLabel { get; set; }
public bool LogUnhandledErrors { get; set; } = true;
public int MaxTextLength { get; set; } = 1_048_576;
public int MaxArrayLength { get; set; } = 1_048_576;

public CompileResult Compile();                                   // idempotent; returns diagnostics
public ScriptResult Run();
public FunctionHandle? GetFunction(string name, int arity);
public ScriptResult Call(FunctionHandle fn, ReadOnlySpan<WarValue> args);
public ScriptResult Call(FunctionHandle fn, params WarValue[] args);
public ScriptResult StartCoroutine(string name, ReadOnlySpan<WarValue> args, bool loop, out int id);
public ScriptResult TickCoroutines(F64 dt);                       // Value = number of active coroutines

public bool IsHalted { get; }
public ScriptError? Fault { get; }
public event Action<WarScriptLanguage, ScriptError>? Faulted;
public void ClearFault();
public void Reload(string newSourceCode);                         // existing semantics; also clears the fault

public void RegisterNativeFormatter<T>(Func<T, string> format);   // PROP-14
public ulong ComputeStateHash();                                  // T0.6, canonical encoding in 9.7
```

`CompileResult` holds a success flag and a list of diagnostics. Each diagnostic has a severity, a message, a script name, a line and a column. (T2.2: `CompileResult.Success`, `CompileResult.Diagnostics` of `CompileDiagnostic` with `Severity` of type `DiagnosticSeverity` { `Error`, `Warning` }, `Message`, `ScriptName`, `Line`, `Column`; the constructor no longer lexes, so a lexer error is a diagnostic too.) (T2.3: the types are as above, in `Runtime/ScriptResult.cs`; `ScriptErrorKind` is in `WarScript`, `ScriptErrorException` stays in `WarScript.Exception`; `ScriptError` also holds an internal `CompileException`; `ScriptResult.ToString()` is `Status` or `Status: <error>`.)

### 6.2 Entry points

| Entry point | Outermost entry? | Behavior |
|---|---|---|
| `Compile()` | Not an entry | Parses the source, resolves imports (Phase 3), compiles and verifies. Runs no script code. Calling it again returns the cached result. |
| `Run()` | Yes | Compiles if needed, then runs module initialization: imported files first (Phase 3), then the top-level code. |
| `Call(fn, args)` | Yes, unless it is a nested entry (6.5) | Runs one function. Missing arguments are null, so default parameters apply. The result's `Value` is the function's return value. |
| `StartCoroutine(...)` | Yes | Creates a coroutine and runs it until its first yield. |
| `TickCoroutines(dt)` | Each resume is an outermost entry | Resumes ready coroutines newest first (PROP-24) and stops at the first fault (PROP-3). |
| `Reload(source)` | Not an entry | Keeps v1 semantics: globals are kept, definitions are replaced and coroutines are stopped. It also clears the fault. |

### 6.3 Status meanings

| Status | Meaning | Value | Error |
|---|---|---|---|
| `Ok` | The entry completed. | The return value | null |
| `Error` | A script error (any kind except HostFault) escaped the entry. The instance is now halted. | null | The error |
| `Halted` | The instance was already halted, so nothing ran. | null | The original fault |
| `HostFault` | An unexpected C# exception occurred inside the VM. The instance is halted and its context stacks have been restored to the entry depth. | null | The error, with `HostException` set |

### 6.4 Halting

An unhandled error, or a HostFault, at an outermost entry halts the instance. When that happens, the runtime takes these steps in order:

1. Unwinds all frames.
2. Restores the context stacks.
3. Clears all coroutines.
4. Sets `IsHalted` and `Fault`.
5. Logs the error with `ScriptError.ToString()` if `LogUnhandledErrors` is true.
6. Invokes `Faulted` synchronously.

While an instance is halted, every entry returns `Halted` immediately, without running code or consuming budget. That includes entries made from a `Faulted` handler.

`ClearFault()` and `Reload()` return the instance to normal operation. Neither undoes state changes made before the fault. Native calls made by a handler before it failed stay applied; that is consistent across peers, and F-SIM-1 describes an optional command-buffer design if it ever matters for gameplay.

### 6.5 Nested entries

An entry is nested when it starts while the same instance is already executing. That happens when a script calls a native and the native calls `Call` on the same instance, for example to fire an event handler.

- If the instance's cached VM is busy, the nested call runs on a separate `WarVM` (DEC-16). A busy VM is never reset.
- Nested entries share the outermost entry's instruction and memory budgets (PROP-2).
- Nesting deeper than `MaxHostNesting` raises LimitExceeded.
- If the nested call ends with an unhandled error, the native receives an `Error` result, and the error stays pending. When the native returns, the error continues propagating in the calling script from the native call site, so it can be rescued there (PROP-1). It halts the instance only if it escapes the outermost entry.
- In v2, a native cannot suppress a callback error. F-LANG-5 records an isolated-call variant as a possible future addition.

A native that calls a *different* instance makes an outermost entry into that instance, with its own budgets and its own fault state. The calling instance is unaffected unless the native raises an error into it.

### 6.6 Determinism of faults

Every error kind except HostFault is deterministic, provided the natives are deterministic. Budgets count instructions and estimated bytes, not wall-clock time or real memory, and all limits are fixed constants or per-instance configuration that is identical on every peer. So all peers halt the same instance at the same instruction, and the simulation stays in sync.

HostFault is not assumed to be deterministic. It covers VM bugs, out-of-memory conditions and failures in host infrastructure. The host should treat it as a desync risk, for example by reporting it and resynchronizing or ending the session.

The host has four obligations:

1. Drive instances and their entry points in a deterministic order.
2. Keep natives deterministic, including when and how they throw.
3. Treat each instance's halted flag as part of simulation state for save, replay and rejoin (Q-5).
4. Never make simulation decisions based on `HostException` details.

Messages generated by the runtime are deterministic by construction (R1).

### 6.7 Multiple instances

Instances share no mutable runtime state (R1, T1.9). What a room or map does when one of its instances halts is host policy (Q-4); the `Faulted` event and `InstanceLabel` exist to support it. Passing values between instances is governed by Q-1. Until the owner decides otherwise, the default is:

- Plain data may cross instances: numbers, text, arrays, and reading or writing fields of a foreign instance.
- Calling a method of, or a function value owned by, another instance raises TypeError with the message "belongs to another script instance".

Implementing that default requires an owner reference on each `ClassData` and a small function-value wrapper that holds the owning instance (T3.5, if Q-1 confirms the default).

### 6.8 Logging

`Logger` still receives `print` output. For unhandled errors it receives `ScriptError.ToString()`, unless `LogUnhandledErrors` is false. Every runtime log line is prefixed with `InstanceLabel` when one is set.

### 6.9 Rules for native functions

Natives receive their arguments as today; the ABI change is deferred to F-PERF-2.

| A native that... | Becomes |
|---|---|
| Throws `ScriptErrorException(kind, message)` | A script error of that kind |
| Hits an argument validation failure in `NativeHelper` | ArgumentError or TypeError (`NativeHelper` throws `ScriptErrorException`) |
| Throws any other exception | NativeError with the exception's message. It can be rescued. |

Natives may call back into their own instance (6.5) or into other instances. They must be deterministic.

---

## 7. Implementation plan

### 7.0 Overview

| Phase | Goal | Tasks | Gate (done when) |
|---|---|---|---|
| 0 | Test infrastructure | T0.1 to T0.9 | The headless suite and goldens are green, except documented known failures. Invariant checks, bytecode round-trips and the culture matrix run on every test. The fuzzer runs. A test-suite audit report exists. No game-side step (DEC-20). |
| 1 | Crash, security and determinism fixes, with no syntax changes | T1.1 to T1.10 | Every CRASH, BUDGET, DET and SEC repro passes. The fuzzer runs clean for 60 minutes. A test proves the budgets cannot be escaped through constructors, imports or re-entrant calls. Bytecode format v2 is in place. |
| 2 | Host API v2 and removal of the tree-walker | T2.1 to T2.9 | No tree-walk code remains. Every entry point returns `ScriptResult`. Per-instance halting works. Error kinds and call stacks are covered by tests. |
| 3 | Language v2 and migration | T3.1 to T3.11 | All scripts are migrated. The suite is green under v2 semantics. Every migrated script, the game's included, compiles with no errors against the native manifest (PROP-30). The language spec is published. |
| 4 (deferred) | Performance | Section 11.1 | Not scheduled |

**Ordering.**

- Phase 0 comes first because Phase 3 is verified against its test suite, and Phase 1 fixes need regression tests.
- Phase 1 comes before Phase 3 because its fixes are small and independent, and running constructors as frames (T1.1) is the basis for the frame-based design in T3.5.
- Phase 2 comes before Phase 3 because removing the tree-walker halves the number of places semantics live, so each Phase 3 change lands once.
- Within Phase 1, do T1.3 (exception boundary) early, because later tasks rely on it.
- T1.7 depends on the `OpInfo` table, and the disassembler fix falls out of it.
- In Phase 3, T3.1 must precede T3.2, and T3.2 must run on the v1 frontend before T3.3 replaces it.

### 7.1 Phase 0: tests and baseline

#### T0.1 Headless build and test projects

**Change.**
- Create `Headless~/` (PROP-19) containing:
  - `WarScript.Runtime.csproj`: `netstandard2.1`, C# 9.0; compiles `../Runtime/**/*.cs`, `../Attributes/*.cs` and the FixPointCS sources.
  - `WarScript.Tests.csproj`: `net8.0`, NUnit 3.14; references the runtime project and compiles `../Tests/**/*.cs`.
  - `Directory.Build.props`: defines a `FixPointCSDir` property.
  - `fetch-fixpointcs.sh`: clones the pinned FixPointCS commit into `Headless~/external/FixPointCS` when `FixPointCSDir` is not set.
- Add `Headless~/bin/`, `Headless~/obj/`, `Headless~/out/` and `Headless~/external/` to `.gitignore`.
- Add `Runtime/AssemblyInfo.cs` containing `[assembly: InternalsVisibleTo("WarScript.Tests")]`, with a `.meta` file (R3).

The project files are in section 9.1. The runtime compiled unmodified against upstream FixPointCS; if the game uses a modified copy, point `FixPointCSDir` at that copy instead.

**Done when.** `dotnet test Headless~/WarScript.Tests.csproj` builds and runs the existing 832 tests. Any test that fails only because of the headless environment is fixed or documented. The command is recorded in `CLAUDE.md`.

#### T0.2 Golden script runner

**Change.**
- Implement the format in 9.2 as `Tests/Golden/GoldenRunner.cs`. The new `Tests/Golden/` folder and the file both need `.meta` files (R3). It discovers `Tests/Golden~/**/*.ws` files (which need no `.meta` files), runs each one, and checks its expectations.
- Convert the 83 `Tests/resources/*.ws` scripts into goldens. Most already use `assert`, which means "completes without an unhandled error". Add `# expect:` lines where they print.
- Where an existing C# test only runs a snippet and compares printed output, move the snippet into a golden file. This matters for Phase 3, because the migration tool processes files, not string literals inside C# code.
- Keep C# tests that exercise host APIs as C#.

**Done when.** All converted goldens pass or are marked `# known-failing:` with a finding ID, and adding a test means adding one file.

#### T0.3 Invariant checks

**Change.** Wrap every script execution in the tests, both in `TestHelper` and in the golden runner, with the checks in 9.3. Add internal accessors where needed, such as `DefinitionContext.Depth`, `ClassInstanceContext.Count` and `WarVM.IsIdle`.

**Done when.** Invariants run on every test, and a violation fails the test with the name of the invariant.

#### T0.4 Bytecode round-trip and culture matrix

**Change.**
- The golden runner executes each file in two modes: from source, and from bytecode (compile from source, `SaveBytecode`, new instance, `LoadBytecode`, run). Outputs must match.
- The whole golden suite runs under each culture listed in 9.4. Set `CultureInfo.CurrentCulture` and `CurrentUICulture` for each test and restore them afterwards.

**Done when.** Both modes and all cultures run as part of the default test command. DET-1 failures show up as known failures until T1.6.

#### T0.5 Known-bug corpus and child-process runner

**Change.**
- Add every repro from Appendix A as a golden file, with its v2-expected outcome and `# known-failing: <ID>`.
- Build `Headless~/WarScript.Runner`, a console app that runs one script file and prints its output lines, result and invariant status as JSON. It exits with code 0 unless the process itself misbehaves.
- Files marked `# isolate` run through the runner in a child process, because a .NET stack overflow kills the whole process.
- These tests carry `[Category("HeadlessOnly")]` and are skipped inside Unity.

**Done when.** The corpus runs, and the repros that abort the process are contained in child processes and reported as known failures.

#### T0.6 State hash

**Change.**
- Implement `WarScriptLanguage.ComputeStateHash()` with the canonical encoding in 9.7. Making it public is useful to the host for diagnosing desyncs.
- The golden runner writes one `<file>: <hash>` line per mode into `Headless~/out/golden-hashes.<os>-<arch>.txt`.
- Add `Headless~/compare-hashes.sh`, which diffs hash files from different machines.

**Done when.** Hashes are identical across repeated runs on one machine and, after T1.6, across the culture matrix.

#### T0.7 Fuzzer v0

**Change.** Add `Headless~/WarScript.Fuzz`, a console app as described in 9.6, with the options `--minutes`, `--seed` and `--corpus`. Findings are saved with their seed under `Headless~/out/fuzz/`.

**Done when.** A 10-minute run works end to end. Each crash it finds maps to a known finding or is reported to the owner as a new one.

#### T0.8 Audit mode (temporary, v1 semantics)

**Change.**
- Add an optional `WarScriptLanguage.AuditSink` (`Action<AuditEvent>`). Emit the events listed in 8.1 from the v1 code paths whose semantics change in Phase 3. When the sink is null, the cost is one null check on paths that are already slow (mixed types, fallbacks, errors).
- Add an `AuditReport` that de-duplicates events by (kind, script, line), counts them, and writes Markdown or JSON.
- Run the full test suite with auditing on and commit the report as `Documentation~/migration/audit-tests.md`.

**Owner step.** Dropped (DEC-20). v1.0 of this plan asked the owner to run game sessions with the sink attached and return the report; nothing waits for that report any more. `Documentation~/migration/AUDIT.md` still explains how to record one if the owner wants it.

**Done when.** The test-suite report is committed.

#### T0.9 Behavior baseline (dropped, DEC-20)

Dropped in round 4: Phase 3 is verified against the test suite and the compiler, not against recorded game scenarios. What was built stays, unrequired: `Documentation~/migration/BASELINE.md` (the recording instructions, the review of run-1 and the hashes of the headless warman trace) and `Headless~/ModTrace`. The original task, for the record:

**Change.** Write `Documentation~/migration/BASELINE.md`, explaining how to record a baseline with the current runtime: for each scenario, the printed output plus a per-tick `ComputeStateHash()` for each instance. Claude Code cannot run the game.

**Owner step.** Record baselines for representative scenarios before Phase 3 starts.

**Done when.** The baseline files exist, wherever the owner prefers to keep them, and their location is noted in `BASELINE.md`.

### 7.2 Phase 1: crash, security and determinism

#### T1.1 Constructors run as frames on the current VM

Fixes CRASH-2, BUDGET-1 and PERF-1.

**Change.** Remove `new WarVM(_script).Run(definition.CompiledConstructor)` from `InstantiateClassDefShared`.

1. Split instantiation into an allocation step. It builds the complete instance graph without running any constructor body:
   - the derived `ClassData`;
   - the base-class `ClassData` objects in `Relations`;
   - shared `ValueReference`s for constructor parameters passed to base classes;
   - property values initialized from the arguments.

   It returns the constructors to run, in execution order: base classes depth-first in `BaseTypes` order, then the derived class. This must match v1's order; `test_classes_inheritance.ws` and `test_deep_multi_inherit.ws` verify it.
2. `NewInstance` and `NewNestedInstance` push the new instance onto the operand stack as the expression's result. They then push one constructor frame per constructor body, in reverse execution order, so the first constructor to run is on top.
3. Replace the `IsMethodCall` and `HasScope` booleans on `CallFrame` with a `FrameKind` enum (`Script`, `Method`, `Constructor`). A constructor frame records its `ClassData`.
   - When a constructor frame is pushed, it pushes the class `MemoryScope`, the `ClassInstanceContext` entry and the class `DefinitionScope`, exactly as the old code did.
   - `DoReturn` and `DoHandleException` pop them.
   - A constructor frame's return value is discarded.
4. Check that there is frame capacity for all the constructor frames before pushing any of them. If there is not, raise LimitExceeded "Stack overflow" and leave the stack unchanged.
5. Account allocations before allocating (T1.4).

An error inside a constructor unwinds through constructor frames like any other frame, and can be rescued by the code that executed `new`. Phase 3 changes how field access is compiled, but keeps this frame design.

**Tests.**
- Appendix A.3 and A.7.
- Inheritance-order goldens are unchanged.
- An error inside a base-class constructor is rescued by the caller.
- 20,000 instances whose class has a constructor body allocate less than 1 KB each. This is a headless allocation test using `GC.GetAllocatedBytesForCurrentThread`, marked `[Category("HeadlessOnly")]`.

**Done when.** No code path creates a `WarVM` to run a constructor.

#### T1.2 Cycle-safe, depth-limited value operations

Fixes CRASH-1.

**Change.**
- Give `WarValue.ToString` a private recursive formatter that carries the list of arrays currently being formatted and a depth counter. An array already in that list, or any value nested deeper than `MaxValueDepth`, formats as `[...]`.
- Give `WarValue.Equals` and `GetHashCode` a depth counter.
  - Beyond `MaxValueDepth`, `Equals` throws an internal `WarScriptLimitException`, which T1.3 converts to LimitExceeded.
  - `GetHashCode` hashes at most the first 8 elements and 3 levels deep. That stays consistent with `Equals`: equal values still hash equal.
- Apply the same depth guard to `ClassData.StructuralEquals` and `StructuralHashCode`, until DEC-9 removes structural class equality in T3.6.
- Pass the tracking state as parameters rather than using static fields (R1).

**Tests.**
- Appendix A.1 and A.2.
- An array nested 10,000 levels deep prints and compares without crashing.
- The natives `Array_contains`, `Array_remove` and `Array_index_of`, which use `Equals`, raise LimitExceeded on cyclic arrays instead of crashing.

Implementation notes (T1.2):

- `MaxValueDepth` is the constant `WarValue.MaxValueDepth` (256), shared with `StateHash`. It is not an instance setting: `WarValue.Equals` and `ToString` have no instance to read it from, and PROP-5 makes only the text and array limits configurable.
- Depth counts arrays and instances: the outermost value is at depth 0, so 256 nested arrays compare and the 257th raises LimitExceeded "Value nested too deeply" (recognised as LimitExceeded by the golden runner). An instance's properties are one level below it.
- Formatting tracks the arrays on the current formatting path only, so the same array twice side by side (`{s, s}`) still prints in full. Instances format as their class name and are not followed.
- `GetHashCode` includes the element count, then at most the first 8 elements (or properties of an instance) down to 3 levels.
- There is no reference-equality shortcut for arrays: `a == a` with a cyclic `a` raises LimitExceeded, as the plan's rule for equality says.
- Review fix (BUDGET-3, A.27): depth limits do not bound the work on shared arrays. Equality now carries a memo of the array and instance pairs it has found equal (created on the first pair with nested arrays or instances, so flat comparisons allocate nothing) and compares each pair once; a memo hit raises the depth error exactly when the full walk would have, because each pair records the height of its comparison. Only equal pairs are recorded: an unequal pair ends the comparison anyway, and a pair is recorded after its comparison ends, so a cycle still reaches the depth limit. Formatting stops adding elements once the text reaches `WarValue.MaxFormattedLength` (1,048,576 characters, the default `MaxTextLength`; a constant for the same reason as `MaxValueDepth`) and writes `...` in place of the remaining elements of every open array, so the result is at most a few thousand characters longer than the cap. As a consequence, mixed-type `==` (which compares formatted text until T3.6) only sees the first 1,048,576 characters of an array. `StateHash` is still exponential on shared arrays (Q-13).
- Tests: A.1 and A.2 pass (they stay `# isolate` as a guard); golden `language/value_depth.ws` (cycles, 10,000 levels, the boundary at 256, `!=`, the three natives, nested instances); `Tests/ValueDepthTests.cs` (host-side `Equals`/`GetHashCode`/`ToString`). The fuzzer's CRASH-1 entry is removed.

#### T1.3 One exception boundary, with context restoration

Fixes CRASH-3.

**Change.** At each outermost entry (`Run`, `RunFunction`, `InitCoroutine`, `ResumeCoroutine`), record the depths of `MemoryContext`, `DefinitionContext` and `ClassInstanceContext`. Then, in `Execute()`:

- Catch `WarScriptLimitException` and `ScriptErrorException` (6.9) and raise the corresponding script error. Continue through `DoHandleException`, so scripts can rescue it.
- Wrap the native invocation in `CallMethod` in the same try/catch that `Call` already uses, so its exceptions become NativeError.
- Treat any other exception as HostFault:
  - restore the recorded context depths;
  - reset the VM;
  - in Phase 1, log the fault with its C# stack trace and stop execution;
  - from Phase 2, return a HostFault result and halt the instance.

Keep the existing catch for value-stack overflow until T1.7 provides `MaxStack`.

Follow-up (CRASH-8, Q-12 default; done after T1.2):

- `break` and `next` leave the `begin` blocks opened inside their loop like `return` does, innermost first. The compiler tracks the `begin` block being compiled and which part of it (begin body, rescue body or ensure body). For each block left it emits: the `PopScope`s and `PopN` down to the block's entry, then `PopHandler` from the begin body, or the new `DropPending end` from the ensure body (it discards that block's pending re-raise or return, as `raise` and `return` inside an ensure block already do, CRASH-7), and then, unless the jump is inside the ensure body, a copy of the ensure body. The rescue body needs neither: its handler is already gone. Errors raised by an inlined ensure body go to the enclosing handlers, as they would from the original.
- `DropPending` is appended at the end of `OpCode` so no existing opcode value changes (they stay implicit until T1.7). Bytecode compiled before this change does not contain it and runs as before, keeping CRASH-8.
- `break`/`next` restore the compiler's locals, runtime scope depth and `begin` stack after emitting the jump, so the unreachable statements after them compile against the block they are in. This also fixes SEM-12.
- Tests: A.26 passes; golden `language/break_out_of_begin.ws` (begin, rescue and ensure bodies, nested blocks in a function, the pending error and pending return dropped, an outer handler kept, SEM-12). The fuzzer's CRASH-8 entry is removed.

**Tests.**
- Each repro that used to leak a C# exception now produces either a rescuable script error or a HostFault, and invariants hold afterwards in every case.
- A test native that throws `InvalidOperationException` produces a NativeError that `begin/rescue` can catch.

#### T1.4 Allocation guards

Fixes CRASH-3.

**Change.** Check sizes before allocating, using `long` arithmetic:

- **Text repetition.** A count of 0 or less gives `""`. A result longer than `MaxTextLength` raises LimitExceeded "Text too long".
- **Text concatenation.** The same limit.
- **Arrays.** `AddArrays`, `ArrayAppend` and array natives such as `Array_insert` are checked against `MaxArrayLength`.
- **Memory budget.** Every `TrackAlloc` call site checks the return value before allocating and raises MemoryBudgetExceeded immediately.
- **Text subtraction.** Until Phase 3 removes the operator, `text - ""` returns the left operand unchanged.
- **Configuration.** Add `MaxTextLength` and `MaxArrayLength` properties to `WarScriptLanguage` (PROP-5).

**Tests.**
- Appendix A.4 and A.5.
- For each guarded operation, a size exactly at the limit succeeds and one past it fails.
- The memory budget raises its error before the allocation happens; an allocation test checks this.

#### T1.5 Re-entrant host calls and instance-level budgets

Fixes CRASH-4 and BUDGET-1. Implements DEC-16, PROP-1 and PROP-2.

**Change.**

1. Move the instruction and memory budget counters from `WarVM` into a per-instance budget object on `WarScriptLanguage`. Reset it only when an outermost entry starts. Every VM of the instance charges the same counters: the cached VM, coroutine VMs, and the import VMs until Phase 3 removes them.
2. Track `EntryDepth` on the instance. Each entry increments it on start and decrements it in a `finally` block. Going past `MaxHostNesting` raises LimitExceeded.
3. Mark the cached VM as busy while it executes. If an entry arrives while it is busy, run the call on a separate `WarVM` (DEC-16; pooling is F-VM-2). Never `Reset()` a busy VM.
4. Error propagation (PROP-1):
   - When a nested execution ends with an unhandled error, leave the error raised in the instance's exception state and return to the native. From Phase 2, the native receives an `Error` result.
   - When the native returns, the calling VM's existing check after native calls finds the raised error and continues unwinding in the calling script.

**Tests.**
- Appendix A.6: a native that calls back into the same instance receives the callback's return value, and the outer script continues normally.
- A callback error is rescued by a `begin/rescue` around the native call in the outer script.
- An unrescued callback error ends the outer call with that error.
- Mutual recursion between a native and a script function stops at `MaxHostNesting` with LimitExceeded.
- Instructions executed inside the callback count against the outer call's budget.

#### T1.6 Culture-invariant, integer-only formatting

Fixes DET-1, DET-2 and DET-3. Implements PROP-14.

**Change.**

1. Use `CultureInfo.InvariantCulture` for every number-to-text conversion in `Runtime/`, including error messages built with string interpolation. Use `FormattableString.Invariant` or an explicit `ToString(CultureInfo.InvariantCulture)`.
2. Rewrite `WarValue.FormatFractional` without exceptions or `double`:
   - For each p from 1 to 9, compute the rounded p-digit candidate with integer arithmetic, as today.
   - Check each candidate with a new non-throwing `NumericLiteral.TryParseRaw`, which uses integer-only arithmetic and the same truncation as the parser. Return the first candidate that reproduces the raw value.
   - If no candidate does, emit the exact expansion: repeatedly multiply the 32-bit fraction by 10, emit the integer digit, and keep the remainder, until the remainder is 0. This takes at most 32 digits.
   - Handle negative values and `F64.MinValue`, where negating the raw value overflows; use unsigned arithmetic.
3. Replace the `NativeObject` branch of `ToString` with the rules in 5.10:
   - `F64Vec3` formats component by component with the integer formatter;
   - function values format as `<fun name>`;
   - other native objects format as `<TypeName>` unless a formatter is registered through a new `WarScriptLanguage.RegisterNativeFormatter<T>(Func<T, string>)`.

As an optional extra defense on the host side, the owner can set the simulation thread's culture to invariant.

**Tests.**
- The culture matrix is green.
- A formatting table produces identical output in every culture for 0, -1500, 1000, 0.5, -0.5, 99.5, 3.14, 0.7, 1/3, `F64.MinValue`, `F64.MaxValue` and `F64.FromRaw(1)`.
- `F64Vec3` values print as specified.
- A source scan in the test project finds no numeric `ToString()` call in `Runtime/` without a culture argument.

**Note.** The exact expansion can be long: 1/3 prints as `0.33333333325572311878204345703125`. That is the agreed behavior. If the owner later prefers a shorter rounded display form, it must still use integer arithmetic only.

#### T1.7 Opcode table, bytecode format v2 and verifier

Fixes DET-4, SEC-1, SEC-2, SEM-10 and DX-5. Implements DEC-18 and PROP-18.

**Change.**

1. **Explicit opcode numbers.** Give every `OpCode` member an explicit numeric value.
   - Remove the unused opcodes `YieldUntil`, `EqualJump`, `NotEqualJump`, `LessEqualJump`, `GreaterJump` and `GreaterEqualJump`. First confirm with a search that the compiler emits only `LessJump`.
   - Do not reuse the removed numbers.
2. **`OpInfo` table.** Add a single table (10.1) describing each opcode's operands and stack effect, and rewrite `Chunk.DisassembleInstruction` to use it.
3. **Format version 2.**
   - Bump the format to version 2 and add the format hash to the header; the loader requires an exact match (10.2, PROP-18).
   - Add a test that recomputes the format hash from `OpInfo` and compares it with the recorded constant. If they differ, it fails with the message "opcode table changed: bump BytecodeFormat.Version and update BytecodeFormat.Hash".
4. **Line table.** Replace the per-byte line list with a run-length line table (10.2), both in memory and in the file.
5. **Loader hardening.** Harden the loader as described in 10.4.
6. **Verifier.** Implement `BytecodeVerifier.Verify(CompiledFunction)` with the checks in 10.3, including the stack-height analysis that computes `MaxStack` for each function.
   - Run the verifier on every function at load time, always, and on compiler output in tests and Debug builds.
   - When pushing a frame, the VM checks that `StackBase + MaxStack <= StackMax` (plus locals; see T3.5) and raises LimitExceeded "Value stack overflow" if not.
   - Then remove the `IndexOutOfRangeException` filter from `Execute`.
7. **Dispatch default case.** Add a `default:` case to the dispatch switch that raises HostFault "invalid opcode". It should be unreachable once the verifier is in place.

**Tests.**
- The compiled output of every golden program passes verification.
- A disassembly test checks that the instruction boundaries reported by the disassembler match the verifier's, for every compiled function.
- A unit test with a hand-built malformed function for each rejection listed in 10.3.
- A bytecode-mutation mode in the fuzzer (9.6): every mutated file is either rejected with `InvalidDataException` or runs without any C# exception escaping.
- Appendix A.19 (already in `Tests/ReproTests.cs` as known-failing DX-5).
- Appendix A.20. It was not added in T0.5 because its cases are defined against format v2 (format hash, verifier); add it with this task.

#### T1.8 Compiler limit checks

Fixes DX-2.

**Change.** Introduce a `CompileError` exception (a subclass or replacement of `SyntaxException`) that carries the script name and line. Then:

- `EmitU16` throws if the value exceeds 0xFFFF.
- `PatchJump`, `EmitLoop` and the patching of exception-handler addresses raise "function too large (bytecode exceeds 65,535 bytes)".
- Adding a 65,536th constant raises an error.
- More than 255 arguments in a call raises an error.
- A `PopN` of more than 255 is split into several `PopN` instructions.
- An array literal with more than 65,535 elements raises "array literal too large; build it with <<".

**Tests.** For each limit, a test with generated source that sits exactly at the limit and one that goes past it.

#### T1.9 Lexer cache and static state

Fixes DX-1 and DX-6. Implements DEC-19.

**Change.** Replace the static `Dictionary` in `LexicalParser` with a bounded LRU cache, 64 entries by default, guarded by a lock. It returns `IReadOnlyList<Token>`. Keep `ClearCache()`.

Add a reflection test over all types in `WarScript.Runtime` that fails on any static field that is neither `readonly` nor `const`. It also fails on `readonly` static fields of mutable collection types, except those on an explicit allow-list. The allow-list contains the synchronized lexer cache and the tables that are initialized once and never mutated afterwards: `OperatorExtension.OperatorMap`, `StringInterner.IntegerStrings` and `WarScriptLibraryRegistry.Libraries`. Each allow-list entry carries a comment explaining why it is safe.

**Tests.** The reflection test, and a stress test that lexes from several threads at once.

#### T1.10 Complete line information

Fixes DX-3.

**Change.** Pass the real line number at every emit site. Search `Compiler.cs` for `EmitByte(` and `EmitU16(` calls that pass a literal `0`; they are in the call, `new` and nested-`new` paths of `CompileExpression`. Runtime errors, stack traces and breakpoints then report correct lines.

**Tests.** A golden test for each instruction family (function call, method call, `new`, property access, indexing, arithmetic) that checks the line number reported by a runtime error.

### 7.3 Phase 2: host API and tree-walker removal

#### T2.1 Remove the tree-walk interpreter

Fixes API-3. Implements DEC-2.

**Change.**

1. Delete `Execute()` from `Statement/*` and `Evaluate()` from `Expression/*`.
2. Delete `Coroutine/Coroutine.cs`, `WarScriptLanguage.CallTreeWalk` and the fallback branch in `Call`.
3. Delete `Context/BreakContext.cs`, `BreakScope.cs`, `NextContext.cs`, `NextScope.cs`, `ReturnContext.cs` and `ReturnScope.cs`, together with their fields.
4. Remove `HaltFlags` once the VM no longer needs it. Yield state moves into the VM and coroutine; exception state moves into the error model of T2.5.
5. Remove `ExceptionContext.AddTracedStatement` and the `List<Statement>` stack trace in `Exception/Exception.cs`.
6. Make `ValueReference` a plain storage class that no longer implements `IExpression`.
7. Update the tests: in `ParserTests.cs`, replace `ConstantExpression.Evaluate()` with `.Value`; adjust `ImportBytecodeTests.cs`.
8. Delete the `.meta` file of every deleted file (R3).

AST nodes become plain data; `ConstantExpression` keeps its `Value`. The compiler does not call `Evaluate`; only a comment mentions it, so update the comment.

Optional but recommended: collapse the operator classes in `Expression/Operator/` into `BinaryExpression` and `UnaryExpression` nodes that carry an `Operator` enum. This simplifies the compiler's expression switch.

**Done when.** Searching `Runtime/` for `Evaluate(` and `.Execute(` finds nothing related to executing the AST, and all tests pass.

#### T2.2 Explicit compile step

**Change.** Add an idempotent `CompileResult Compile()`.
- It does not throw for script errors; it returns diagnostics.
- `Run`, `Call` and `StartCoroutine` call it when needed.
- A compile error produces an `Error` result of kind CompileError and halts the instance.
- No code path executes AST nodes any more.

#### T2.3 Result types

Fixes API-1 and API-2. Implements DEC-12.

**Change.** Add the types in 6.1, and change `Run`, `Call`, `StartCoroutine` and `TickCoroutines` to return `ScriptResult`. `Call` returns the function's return value, which `RunFunction` already computes.

#### T2.4 Fault and halt model

Implements DEC-12, DEC-13 and PROP-3.

**Change.** Implement 6.4:
- `IsHalted`, `Fault`, the `Faulted` event and `ClearFault()`;
- `Reload` clears the fault;
- a halted instance answers every entry with `Halted`;
- coroutines are cleared on a fault;
- the `LogUnhandledErrors` setting.

#### T2.5 Structured errors

**Change.** Replace `RuntimeError(string)` with `RuntimeError(ScriptErrorKind, string)`, and assign a kind at every place an error is raised:

| v1 message (prefix) | Kind |
|---|---|
| "Division by zero", "Modulo by zero" | DivisionByZero |
| "Instruction budget exceeded" | InstructionBudgetExceeded |
| "Memory budget exceeded" | MemoryBudgetExceeded |
| "Stack overflow", "Value stack overflow", "Exception handler stack overflow" | LimitExceeded |
| "Function '...' with N args is not defined", "Class '...' is not defined", "Method '...' not found" | NameError |
| "Cannot access property ... on non-class value", "Cannot call method ... on non-class value", "Value is not callable", "Unable to multiply/divide/... non-numeric values", "Unable to iterate" | TypeError |
| "Native function '...' failed: ..." | NativeError |
| "Assertion error at line N" | AssertionFailed |
| A `raise` statement | Raised |
| "Circular import", "Cannot import", "Import ... not found" | NameError for now; Phase 3 turns these into compile errors |

**Call stack.** Capture it from the VM frames when the error is raised, innermost frame first: function name, script name, line, and from T3.1 the column.

**Rescue value.** The value a script sees in `rescue e` stays the message text (PROP-22).

**Natives.** A native can raise a specific kind by throwing `ScriptErrorException` (6.9). `NativeHelper` throws `ScriptErrorException` with ArgumentError or TypeError.

#### T2.6 Function handles that survive Reload

Fixes part of API-4.

**Change.** `GetFunction` returns a `FunctionHandle` holding the name, the arity and the owning instance. The handle resolves to the current compiled function through a definitions version counter, which `Reload`, import loading and `AddFunction` increment. If a handle's function no longer exists after a reload, calling it returns an `Error` result with NameError.

#### T2.7 Allocation-free call overloads

Fixes the rest of API-4.

**Change.** Add `Call(FunctionHandle, ReadOnlySpan<WarValue>)`, and keep the `params` overload as a convenience. `TickCoroutines` returns a `ScriptResult` whose value is the number of active coroutines.

#### T2.8 Instance label

Implements PROP-4.

**Change.** Add the `InstanceLabel` property and include it in `ScriptError` and in every log line the runtime emits.

#### T2.9 Documentation

**Change.** Update the host-API sections of `README.md` and `CLAUDE.md`: usage of `Run` and `Call`, error handling and coroutines.

### 7.4 Phase 3: language v2 and migration

#### T3.1 Columns

**Change.**
- Add a `Column` to `Token`, and have the lexer track it. A tab counts as one column; document this.
- Tokens produced when desugaring interpolation carry the column of their interpolation hole, and the tokens produced from `@` carry the column of the `@`.
- Add columns to the AST nodes that errors are reported on, to `CompileError`, to the line table (10.2) and to `ScriptFrame`.
- Error text becomes `file:line:col: Kind: message`. When the source is available, it is followed by the source line and, on the next line, a caret under the column.

Do this on the v1 frontend first, because the migration tool (T3.2) needs columns.

#### T3.2 Migration tool on the v1 frontend

**Change.**
- Tag the repository `warscript-v1-final` before any change to the v2 frontend.
- Build `Tools~/WarScript.Migrate`, a console app, against that v1 frontend plus the T3.1 columns. It implements section 8.2.

It is a one-time tool: once every script is migrated (T3.9) it can be deleted, and it remains available in history.

**Done when.** The tool rewrites the test corpus and reports the items that need manual work. This is verified by running the rewritten corpus once Phase 3 lands.

#### T3.3 Frontend v2

Implements DEC-4 to DEC-7, PROP-9 to PROP-11, PROP-16 and PROP-21.

**Change.**
- **Keywords.** Add the `global` keyword and treat it as reserved.
- **Lexer.** Turn `@name` into the tokens `this`, `::` and `name`.
- **Parser.** Parse the `global a, b` statement, allowed only in function, method, lambda and class bodies. Allow imports only before all other statements. Build a module structure holding the import list, the top-level statements and the definitions.

Scoping of rescue and loop variables is enforced in T3.4.

#### T3.4 Name resolution and compiler

**Change.** Before compiling each function body, run a resolution pre-pass:

1. Collect the parameters, the assigned names (excluding names declared `global`), and the loop and rescue variables together with the extent of their blocks.
2. Assign slots: parameters first, then locals, then block variables. A block variable's slot may be reused after its block ends.
3. Report:
   - reads before assignment;
   - shadowing violations (PROP-9);
   - misuse of `global` (PROP-10);
   - violations of the lambda rules (PROP-16).

Then compile:
- Reads and writes of locals become `GetLocal`/`SetLocal`; reads and writes of globals become `GetGlobal`/`SetGlobal`.
- Collect each class's fields at compile time: constructor parameters, bare assignments in the class body, and base-class fields. Give each field an index in `ClassDetails`.
- Compile `@x` and `this :: x` to `ThisGetProperty`/`ThisSetProperty`, checking at compile time that the field exists.
- In class bodies, bare names resolve to fields first and then to globals (5.7).

Keep immutable program data (bytecode, constants, metadata) separate from per-instance mutable data (inline caches, globals), so that F-VM-3 stays possible.

**Tests.** Appendix A.18. It was not added in T0.5 because it is written in v2 syntax (`@x`, `global`); add it with this task.

#### T3.5 VM frame layout and scope removal

**Change.**
- When a frame is pushed, reserve `LocalCount` slots initialized to null, then check the stack space against `MaxStack`.
- Remove:
  - the `PushScope` and `PopScope` opcodes;
  - the per-call `MemoryScope` push;
  - the pushes of the class `MemoryScope`, `DefinitionScope` and `ClassInstanceContext` entry for methods and constructors.
- `this` lives in the `CallFrame`.
- Globals live in one flat dictionary per instance, plus the host globals scope. Reads use `TryGetValue` and raise NameError when the name is missing.
- Remove the `MemoryContext` scope stack and its pool if nothing else needs them.
- Method resolution uses the instance's class definitions without pushing any context; call-site caching is F-PERF-1.
- Suspending and resuming coroutines no longer needs to detach and reattach context stacks, so simplify `BytecodeCoroutine`.
- If Q-1 confirms the default, add the cross-instance ownership check from 6.7.

#### T3.6 Operator, equality, indexing and formatting semantics

Implements DEC-8 to DEC-11, PROP-6, PROP-7, PROP-8, PROP-15 and PROP-17.

**Change.** Implement 5.3 to 5.5 and 5.10:
- type checks for arithmetic, comparison, negation and append;
- equality as in 5.4, including root identity for instances (store the root `ClassData` on each relation object when the instance is allocated);
- IndexError for out-of-bounds access;
- TypeError for assigning to a text index (delete the text write-back paths in `IndexSetLocal`, `IndexSetGlobal` and `IndexSetProp`);
- TypeError and NameError for property access;
- `true`/`false` formatting;
- removal of text subtraction, plus the new `Text_remove` native (PROP-7).

Remove `ClassData.StructuralEquals` and `StructuralHashCode`.

#### T3.7 Static imports and module initialization

Implements DEC-7 and PROP-12.

**Change.**
- Resolve imports at compile time and at load time, and detect cycles.
- Initialize modules in the order defined in 5.8.
- Remove the `Import` opcode and `ExecuteImport`, which also removes the import VMs.
- Write the import list into the module header (format v3).
- Report duplicate definitions.
- Keep the `ImportCache` semantics: each file initializes once per instance.

#### T3.8 Compile-time diagnostics

**Change.**
Widened in round 4 (DEC-21): anything the compiler can prove is an error is reported here, before the script runs.

- Report errors for:
  - unknown functions;
  - wrong arity, listing the valid arities;
  - an unknown class in `new`, `is` or `as`;
  - an unknown `@field`;
  - assignment to a constant (move the `ConstantNames` check from the parser to the resolver so it covers imports);
  - a misplaced `global`;
  - a user function shadowing a native (PROP-12);
  - reads of names that are never assigned anywhere and are not known host globals (PROP-27);
  - type errors on statically typed operands (PROP-28);
  - the name checks of PROP-29.
- Report warnings for:
  - unused locals (PROP-26);
  - unreachable code (PROP-29).
- Add the warnings-as-errors option to `Compile()` (PROP-31).
- Collect several diagnostics per compile, recovering at statement boundaries.
- Natives must be known at compile time. Extend `Editor/WsBindingGenerator.cs` to always emit the native signature manifest, and let the compiler take one instead of registered natives (PROP-30). The Runner gets a `--manifest <file>` option, so a mod's scripts can be checked headlessly.

**Tests.** One golden with `# expect-compile-error:` (or `# expect-warning:`) per rule, and one that sits next to the rule and must compile: a dynamic case of each PROP-28 rule (the operand comes from a variable or a call) still compiles and raises the runtime error. The corpus must compile without diagnostics, apart from those goldens.

#### T3.9 Run the migration

**Change.** Follow 8.3.

**Owner step.** Migrate the game's scripts, or give Claude Code their paths (Q-10), and compile them against the native manifest (PROP-30) until there are no errors. No comparison with a recorded game baseline (DEC-20); the owner plays the game as usual, and a runtime error it raises is a v2 error with a kind, a line and a stack (T2.5).

#### T3.10 Specification and documentation

**Change.**
- Write `Documentation~/LANGUAGE.md` from section 5.
- Give every row of the operator table, and every rule in 5.4 to 5.9, a golden test.
- Update `CLAUDE.md` (the syntax quick reference, the scope system, the invariants and gotchas) and `README.md`.

#### T3.11 Bytecode format v3

**Change.** Bump the version and the format hash to cover the Phase 3 changes:
- the frame layout and the meaning of `LocalCount`;
- the removed and added opcodes;
- the module header with its import list;
- columns in the line table.

---

## 8. Migration

Every `.ws` script moves from v1 to v2 semantics: `Tests/resources`, the golden corpus, test sources embedded in C#, and the owner's game scripts.

The migration rests on two static checks: a one-time rewriting tool built on the v1 frontend (T3.2), and the v2 compiler's diagnostics (T3.8), which turn as many v2 runtime errors as possible into compile errors (DEC-21). The audit mode (T0.8) records behaviors that cannot be found by reading source; since round 4 (DEC-20) only its test-suite report is used, and no step waits for reports from game sessions. What neither the tool nor the compiler can see fails loudly at runtime under v2 and is fixed like any other script error.

### 8.1 Audit events (T0.8)

Since DEC-20 the only audit report the plan uses is the test suite's (`Documentation~/migration/audit-tests.md`). The events stay as specified, in case the owner wants to record a game session anyway.

Emit these events from the v1 runtime. To tell scopes apart, give `MemoryScope` a `Kind` set at creation: `Native`, `User`, `Block`, `FunctionLocal`, `ClassInstance` or `MethodLocal`. In v1, the scope is created:

- in `Call`/`CallValue` for function locals;
- by `PushScope` for top-level blocks;
- in `InstantiateClassDefShared` for class instances;
- in `CallMethod` for method locals.

`Get` and `Set` emit an event when they resolve a name in a scope of a different kind than the one they started from.

| Event | Emitted where (v1) | What changes in v2 |
|---|---|---|
| `MixedTypeEquality` | `Equal`/`NotEqual` when the tags differ and neither operand is null | v2 returns false (DEC-8). Behavior changes wherever v1 returned true. |
| `MixedTypeOrdering` | `WarValue.CompareTo`'s fallback when the tags differ | TypeError |
| `LogicalOrdering` | `CompareTo` on two logical values | TypeError (PROP-17) |
| `TextSubtraction` | `Sub` on non-numbers (the text-replace path) | TypeError; use `Text_remove` (PROP-7) |
| `NonTextConcatenation` | `Add`'s string path when neither operand is text, for example `true + 1` | TypeError |
| `LogicalFormatted` | `ToString` of a logical value during `print`, concatenation or interpolation | The output changes from `True`/`False` to `true`/`false` (DEC-11) |
| `NegateNonNumeric` | `Negate` on anything that is not a number | TypeError |
| `ArrayPlusValue` | `AddArrays` with exactly one array operand | TypeError (PROP-6) |
| `AppendToNonArray` | `ArrayAppend` when the target is not an array | TypeError |
| `IndexReadInvalid` | `IndexGet` returning null for an out-of-bounds or non-numeric index, or a non-indexable target | IndexError or TypeError |
| `IndexWriteInvalid` | An `IndexSet*` opcode ignoring a write | IndexError or TypeError |
| `TextIndexAssign` | The text write-back paths in `IndexSetLocal`, `IndexSetGlobal` and `IndexSetProp` | TypeError: text is immutable |
| `PropertyWriteOnNonInstance` | `SetProperty` when the target is not an instance | TypeError |
| `DynamicPropertyCreated` | `SetProperty` or `ThisSetProperty` creating a name that is not in `ClassDetails.Properties`, outside the class body | NameError (PROP-15). Assignments in the class body declare fields and are fine. |
| `UndefinedRead` | `GetGlobal` resolving to nothing | NameError (DEC-5) |
| `GlobalWriteFromFunction` | `Set` starting in a `FunctionLocal` scope and writing into the `User` scope | Needs `global` (the tool also finds this statically) |
| `FieldAccessViaBareName` | `Get`/`Set` starting in a `MethodLocal` scope and resolving in the `ClassInstance` scope | Needs `@name` (the tool also finds this statically) |
| `GlobalShadowedInClass` | A `Get` in a method or class body that returns null although a global of that name exists in the `User` scope | v2 returns the global's value (PROP-20) |
| `BlockVariableEscapes` | At `PopScope`, record the names created in the popped `Block` scope; emit when one of them is later read at top level and v1 returns null | v2 returns the value (DEC-4) |
| `ClassStructuralEquality` | `Equal` on two different instances that compare structurally equal | v2 returns false (DEC-9) |

`AuditReport` groups events by (kind, script, line) with counts and the first value pair seen, and writes Markdown or JSON.

Implementation notes (T0.8):

- `MemoryScope.Kind` (`MemoryScopeKind`, internal) is set where the scope is created: `Native` and `User` in the `WarScriptLanguage` constructor, `FunctionLocal` for `Call`, `CallValue`, `TailCall`, the host's `Call` and a coroutine's scope, `MethodLocal` in `CallMethod`, `ClassInstance` in `InstantiateClassDefShared`; everything else is `Block`.
- `MixedTypeOrdering` is emitted for every use of `CompareTo`'s text fallback: operands of different types, and also two arrays, instances, nulls or native objects. Every one of them is a TypeError in v2.
- `MixedTypeEquality` carries v1's result in its detail, so the report shows where v1 returned true.
- `LogicalFormatted` is emitted by `print` (also for a logical inside a printed array) and by the text path of `+`, which interpolation compiles to. Natives that format values are not covered.
- `IndexWriteInvalid` covers out-of-bounds array writes, non-numeric indexes, targets that cannot be indexed (including text through `IndexSet`, which has no write-back), and `IndexSetProp` on a non-instance. An out-of-bounds write into text is reported as `TextIndexAssign` with "out of bounds, ignored".
- `DynamicPropertyCreated` treats the constructor (`<Class>#ctor`, which runs the class body) as the class body; a method that creates a field is reported.
- `FieldAccessViaBareName` is emitted for reads and writes; `GlobalWriteFromFunction` also counts writes into host globals.
- `BlockVariableEscapes` records the names of every top-level block scope that ends, by `PopScope` or by an error unwinding it. A null read of such a name at top level is reported as `BlockVariableEscapes` instead of `UndefinedRead`; a null read of a global inside a method or class body is `GlobalShadowedInClass`.
- Each chunk records the script it was compiled from (`Chunk.Source`, not serialized), so an event in a function defined by an import names the import. Bytecode-loaded code carries the instance's script name.
- Only the bytecode VM is audited, not the tree-walker.

### 8.2 Migration tool (T3.2)

`Tools~/WarScript.Migrate` parses with the v1 frontend plus columns and builds symbol tables for the whole module set: top-level assigned names in every file, class fields (constructor parameters, class-body assignments, base classes resolved through imports), parameters and loop variables. Its inputs are:

- root paths;
- a list of host globals (`[WsConst]`/`[WsEnum]` names and anything the host sets);
- optionally, a native manifest (Q-2).

It has two modes:

- `--report` writes `migration-report.md`.
- `--rewrite` edits files in place, or writes them to an output directory, and also writes the report.

The tool preserves v1 behavior. It does not fix latent bugs; it makes them visible. For example, every `global` it inserts reproduces a v1 global write, and some of those will turn out to be accidental clobbering (SEM-1) that the owner should change to locals after reviewing the report.

| Rule | Detection | Action |
|---|---|---|
| A function or lambda writes a global | An assignment target that is not a parameter or loop variable, and is assigned at top level in any file of the module set or appears in the host-globals list | Insert one `global a, b` statement as the first statement of the body, listing the names in order of first use. List it in the report. |
| A method uses fields by bare name | A bare name (read or write) that is a field of the class or one of its bases, and is not a parameter or loop variable of the method | Replace it with `@name` |
| A method writes a name that is a global | In v1 such writes created method locals, because globals were invisible in methods (SEM-4) | No rewrite, because v2's default local semantics match. Report it, since the name now shadows a global. |
| A method or class body reads a name that is neither a field nor a local but is a global | In v1 the read returned null (SEM-4) | Report it: v2 will return the global's value. |
| An import comes after other statements | Position in the file | Move it to the top if everything before it is comments, imports or definitions. Otherwise report it, because moving it changes initialization order. |
| A variable is named `global` | Identifier scan | Rename it to `global_` and report it. |
| Text subtraction with a literal | An operand of `-` is a text literal or an interpolated string | Rewrite `a - b` as `Text_remove [a, b]` and report it. In v2 the same case is a compile error (PROP-28); cases the tool cannot see raise TypeError at runtime. |
| A top-level block variable escapes its block | A name first assigned inside a top-level `if` or loop body and read after the block at top level | Report it: v1 read null, v2 reads the value. |
| A dynamic property is created | `obj :: name = ...` or `this :: name = ...` where `name` is not a field of any class in the module set | Report it; the owner must declare the field. |
| A text index is assigned | `x{i} = ...` where `x` is statically text (a literal or a known text variable) | Report it. In v2 the same case is a compile error (PROP-28); other cases raise TypeError at runtime. |

Each report entry has `file:line:col`, the source line, the rule, and the action taken or required.

### 8.3 Procedure

1. **Phase 0 complete.** The goldens, invariants and test-suite audit report are done. No game baselines or game-session audit reports are needed (DEC-20).
2. **Columns and tag.** Do T3.1 on the v1 frontend, then tag the repository `warscript-v1-final`.
3. **Build and report.** Build the tool (T3.2) and run it in `--report` mode over `Tests/Golden~`, `Tests/resources` and the owner's game scripts (Q-10). Most test sources embedded in C# should have moved to golden files in T0.2; migrate the rest by hand, using the report as a guide.
4. **Review.** Review the tool report together with the test-suite audit report. For each manual item, decide the intended v2 behavior; where the right behavior is unclear, ask the owner. For each inserted `global`, the owner decides whether it is intended or should become a local.
5. **Implement and rewrite.** Implement v2 (T3.3 to T3.8) on a branch. Run the tool in `--rewrite` mode over all scripts, apply the manual decisions, and commit the migrated scripts on that branch.
6. **Test.** Run the full suite. Every failure is either a migration miss (fix the script, or the tool and re-run) or a v2 bug (fix the implementation).
7. **Compile the game's scripts.** Compile every migrated game script with the v2 compiler against the native manifest (PROP-30), with warnings as errors (PROP-31), until it is clean. There is no comparison with a recorded game baseline (DEC-20). A runtime error that shows up later in the game is fixed in the script; if it reveals a check the compiler could have made, add that check to T3.8.
8. **Merge and clean up.** Merge, then regenerate all bytecode and mod archives (format v3). Once the owner confirms, remove `Tools~/WarScript.Migrate` and the audit hooks.

### 8.4 Before and after examples

Functions writing globals (DEC-5). The tool preserves v1 behavior; the owner then decides whether each `global` was intended.

```ruby
# v1                                  # v2 (as migrated)
count = 10                            count = 10
fun helper []                         fun helper []
    count = 0                             global count
    loop i in 0..3                        count = 0
        count += 1                        loop i in 0..3
    end                                       count += 1
    return count                          end
end                                       return count
                                      end
```

Methods and fields (DEC-6):

```ruby
# v1                                  # v2
class Unit [hp]                       class Unit [hp]
    armor = 2                             armor = 2               # class body: declares a field
    fun hit [d]                           fun hit [d]
        dealt = d - armor                     dealt = d - @armor  # local + field
        hp = hp - dealt                       @hp = @hp - dealt
        return hp                             return @hp
    end                                   end
end                                   end
```

Top-level blocks (DEC-4): the source does not change, but the behavior does.

```ruby
if true
    made = 1
end
print made        # v1: null    v2: 1
```

Text subtraction (PROP-7):

```ruby
# v1                                  # v2
name = "Sir " + title - "Sir "        name = Text_remove ["Sir " + title, "Sir "]
```

Strict operators (DEC-8): there is no source change. The program now fails loudly where v1 coerced.

```ruby
if hp_text < 10       # v1: ordinal text comparison    v2: TypeError (compare numbers)
```

Globals inside methods (PROP-20):

```ruby
g = 5
class C
    fun m []
        return g      # v1: null    v2: 5
    end
end
```

---

## 9. Test infrastructure

### 9.1 Headless projects

Layout (PROP-19):

```
Headless~/
  Directory.Build.props
  fetch-fixpointcs.sh
  Runtime/WarScript.Runtime.csproj      netstandard2.1, C# 9.0 (Unity constraints)
  Tests/WarScript.Tests.csproj          net8.0, NUnit 3.14
  Runner/WarScript.Runner.csproj        net8.0 console (T0.5)
  Fuzz/WarScript.Fuzz.csproj            net8.0 console (T0.7)
  external/                             (gitignored) FixPointCS checkout
  out/                                  (gitignored) hashes, fuzz findings, reports
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <WarScriptRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..'))</WarScriptRoot>
    <FixPointCSDir Condition="'$(FixPointCSDir)' == ''">$(MSBuildThisFileDirectory)external/FixPointCS</FixPointCSDir>
  </PropertyGroup>
</Project>
```

`Runtime/WarScript.Runtime.csproj`. This is the configuration verified during the review (the runtime compiled with zero errors and zero warnings), with paths adapted to the layout:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>WarScript.Runtime</AssemblyName>
    <NoWarn>CS8632</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(WarScriptRoot)/Runtime/**/*.cs" />
    <Compile Include="$(WarScriptRoot)/Attributes/*.cs" />
    <Compile Include="$(FixPointCSDir)/FixPointCS/*.cs" />
    <Compile Include="$(FixPointCSDir)/Examples/FixMath/*.cs" />
  </ItemGroup>
</Project>
```

`Tests/WarScript.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>WarScript.Tests</AssemblyName>
    <NoWarn>CS8632</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(WarScriptRoot)/Tests/**/*.cs" />
    <ProjectReference Include="../Runtime/WarScript.Runtime.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- Stay on NUnit 3.x: NUnit 4 moved Assert.AreEqual to ClassicAssert (R2). -->
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  </ItemGroup>
</Project>
```

Notes:

- The test project needs access to a NuGet feed for those three packages. The runtime project does not.
- In Unity, FixPointCS is a separate assembly. In the headless build it is compiled into `WarScript.Runtime`. That is harmless, because the types are public.
- `[assembly: InternalsVisibleTo("WarScript.Tests")]` matches the assembly name in both environments.

`fetch-fixpointcs.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)/external/FixPointCS"
COMMIT=a852f05b428a942f8dc274ee516a893ae224e0d4
if [ ! -d "$DIR/.git" ]; then
  git clone https://github.com/XMunkki/FixPointCS.git "$DIR"
fi
git -C "$DIR" fetch --quiet origin
git -C "$DIR" checkout --quiet "$COMMIT"
```

Commands:

```bash
./Headless~/fetch-fixpointcs.sh                       # once, unless FixPointCSDir is set
dotnet test "Headless~/Tests/WarScript.Tests.csproj"
dotnet run --project "Headless~/Fuzz/WarScript.Fuzz.csproj" -- --minutes 10
```

### 9.2 Golden file format

Golden files live in `Tests/Golden~/`, organized by topic. The runner reads directives from the `Comment` tokens produced by `LexicalParser`, so a `#` inside a text literal is never mistaken for a directive.

| Directive | Placement | Meaning |
|---|---|---|
| `# expect: <text>` | After code on a line, or on its own line | The next printed line must equal `<text>` exactly. The single space after the colon is not part of the expected text. The full printed output must equal the ordered list of `expect` values. |
| `# expect-error: <Kind>[: <substring>]` | After code on a line | The run must end with an unhandled error of this kind, raised on this line, whose message contains the substring. |
| `# expect-error: <Kind>[: <substring>]` | On its own line | The same, but the error may be raised on any line. Use this for budget and limit errors. |
| `# expect-compile-error: <substring>` | After code on the offending line | Compilation must fail with a diagnostic on this line that contains the substring (Phase 3). |
| `# expect-warning: <substring>` | After code on the line | Compilation must report this warning (Phase 3). |
| `# budget: <n>`, `# memory-budget: <n>` | Header | Set the budgets. The default instruction budget is 10,000,000, so a runaway test fails instead of hanging. |
| `# isolate` | Header | Run in a child process through the runner (T0.5). |
| `# known-failing: <ID>` | Header | The test is expected to fail until finding `<ID>` is fixed. A test that still fails is reported as Ignored. A test that unexpectedly passes is reported as a failure, which forces the marker to be removed. |
| `# ignore-output` | Header | Do not compare printed output. Use it for legacy `assert`-style scripts; the run must still complete Ok. |
| `# no-stdlib` | Header | Do not register the standard library. |
| `# modes: source` | Header | Restrict the execution modes, for example for a source-only feature. |

Example:

```ruby
# budget: 100000
x = 1 + 2
print x               # expect: 3
print "a{x}b"         # expect: a3b
y = {1, 2}{5}         # expect-error: IndexError
```

### 9.3 Invariants checked after every entry

| Invariant | When |
|---|---|
| No C# exception escaped. From Phase 2, HostFault results are allowed only in tests that expect them. | Every entry |
| `MemoryContext` depth equals its value before the entry. This applies until T3.5 removes the stack. | Every entry |
| `DefinitionContext` depth equals its value before the entry. | Every entry |
| `ClassInstanceContext` is empty. This applies until T3.5 removes it. | Every outermost entry |
| The VM is idle: no frames, stack pointer 0, no handlers, no pending return or exception. | Every outermost entry that did not yield |
| No host entry is still counted as running (`EntryDepth` is 0; added by T1.5). | Every outermost entry |
| The exception state is consistent. In Phases 0 and 1, it is not raised after the entry, because unhandled errors were logged and cleared. From Phase 2, `Fault` is set exactly when the status is `Error`, `HostFault` or `Halted`. | Every entry |
| Outputs are identical in source and bytecode modes. | Golden runner |
| Outputs and state hash are identical across the culture matrix. | Golden runner |

### 9.4 Culture matrix

The suite runs under each of these cultures:

| Culture | Why it is included |
|---|---|
| `InvariantCulture` | Reference |
| en-US | The common case |
| de-DE | Comma as the decimal separator |
| sv-SE, fi-FI | U+2212 as the minus sign (DET-1) |
| tr-TR | Dotted and dotless `i` in casing operations |

Results must be identical across all of them.

### 9.5 Child-process runner

Usage:

```
WarScript.Runner <file.ws> [--mode source|bytecode] [--budget n] [--memory-budget n] [--culture name]
```

It prints one JSON object: `{"output": [...], "status": "...", "error": {...}, "invariants": "ok"|"<violation>", "hash": "<hex>"}`.

| Exit code | Meaning |
|---|---|
| 0 | The script completed, with any script status. |
| 2 | An invariant was violated. |
| 3 | A C# exception escaped. |

The parent process treats an abnormal termination (signal or abort) as a crash. The default timeout is 10 seconds.

### 9.6 Fuzzer

**Source mode.** Starting from the golden corpus, the fuzzer applies one or more mutations, chosen by a seeded PRNG:

- delete, duplicate or swap a token;
- replace an identifier with another identifier from the same file;
- replace a literal with a boundary value: `0`, `-1`, `2147483647`, `-2147483648`, `0.000000001`, `""`, a 10,000-character text, `{}`;
- insert a self-append such as `a << a`;
- splice in a statement from another corpus file;
- wrap a range of statements in `begin ... rescue e ... end`, in a `loop i in 0..1000 ... end`, or in a function that calls itself.

Each case runs through the runner with an instruction budget of 1,000,000, a memory budget of 64 MB and a 10-second timeout. The oracle is:

- the process did not abort;
- no C# exception escaped;
- the invariants held;
- the run finished within the timeout.

Syntax errors, compile errors and script errors are acceptable outcomes. A failing input is saved together with its seed and the runner's JSON output in `Headless~/out/fuzz/<hash>/`. A simple line-based delta-debugging minimizer is optional.

**Bytecode mode**, available after T1.7: compile a corpus file, then flip 1 to 8 random bytes or truncate the result, then load and run it. The oracle is: an `InvalidDataException` at load time, or a run with no escaped exception.

Implementation notes (T0.7):

- `Headless~/Fuzz/` (`WarScript.Fuzz`). Options: `--minutes` (default 10), `--seed` (default from the clock, always printed), `--corpus` (default `Tests/Golden~`), `--out` (default `Headless~/out/fuzz/`), `--jobs` (default the processor count), `--cases n` (a fixed number of cases instead of a duration), `--case <seed>` (regenerate and run one case; which case a seed produces depends on the corpus) and `--replay <case.ws>` (run a saved case as it is, to check a finding after its fix).
- The corpus skips `# isolate` files: they are the known process killers, and every mutation of them would report the same crash.
- Mutations work on a surface tokenization of the fuzzer's own, not on `LexicalParser` tokens: those cannot be turned back into source, and lexing in the fuzzer's process would let a lexer crash take the fuzzer down. Comments are dropped and `#` replaced, so a case carries no directives except the `# budget: 1000000` and `# memory-budget: 67108864` header the fuzzer writes. Each case runs once, from source, under the invariant culture.
- Case `i`'s seed is derived from the run's seed (SplitMix64, not `System.Random`), so a run is reproducible from its seed and the corpus whatever `--jobs` is.
- Findings are grouped by a signature: the kind (crash, host fault, invariant), the message with quoted names and numbers blanked, and for crashes and host faults the WarScript method they happened in (the runner and `GoldenRun` now append it as `(in <method>)`). `<out>/<signature hash>/` holds the shortest case for that signature; `summary.txt` lists every signature with its count. A table in the fuzzer (`KnownFindings`) maps signatures to the findings that cover them; the run exits with code 4 when a signature matches none. Remove an entry when its fix lands.
- Invariant violations after an unhandled error were expected until T1.3 and were attributed to it; since T1.3 they, and the CRASH-7 signatures, are no longer in `KnownFindings`, so they show up as NEW. One attribution is marked with `?` because a different cause would produce the same signature, so check the saved case: a timeout is attributed to BUDGET-2 (the only hang found so far).
- Since T1.3 a C# exception thrown while the VM runs is caught at the entry point as a host fault and logged instead of escaping `Run()`; `GoldenRun` reports it from `WarScriptLanguage.LastHostFault` with the same text as before, so host-fault signatures are unchanged. Run 2026-11-12 after T1.3 on 3 cores (`--seed 12345 --minutes 5`, 103 corpus files): 20,413 cases, 8 signatures, none new and no invariant violation: CRASH-1, CRASH-3, CRASH-5, CRASH-6 and BUDGET-2.
- Since T1.4 CRASH-3 is fixed and its entries are gone from `KnownFindings`. Run 2026-11-12 after T1.4 on 3 cores (`--seed 12345 --minutes 5`, 104 corpus files): 21,668 cases, 4 signatures, none new: CRASH-1, CRASH-6 (two) and BUDGET-2 (the saved case is the A.25 pattern).
- Since T1.1 CRASH-2 and CRASH-5 are fixed and their entries are gone from `KnownFindings`. Run 2026-11-12 after T1.1 on 3 cores (`--seed 12345 --minutes 5`, 105 corpus files): 22,383 cases, 4 signatures, none new: CRASH-1, CRASH-6 (two) and BUDGET-2.
- Since T1.2 CRASH-1 is fixed and its entry is gone from `KnownFindings`. Run 2026-11-13 after T1.2 on 16 cores (`--seed 12345 --minutes 3`, 106 corpus files): 13,347 cases, 4 signatures: CRASH-6 (two), BUDGET-2 and one new, CRASH-8 (A.26), now in `KnownFindings`.
- Since the CRASH-8 fix its entry is gone from `KnownFindings`. Run 2026-11-13 on 16 cores (`--seed 12345 --minutes 3`, 109 corpus files): 10,118 cases, 3 signatures, none new: CRASH-6 (two) and BUDGET-2 (the saved case is the A.25 pattern).
- Since T1.5 BUDGET-2 is fixed; the timeout entry of `KnownFindings` now names BUDGET-3, because the remaining hang is the state hash on shared arrays (Q-13). Run 2026-11-13 after T1.5 on 16 cores (`--seed 12345 --minutes 3`, 110 corpus files): 11,303 cases, 3 signatures: CRASH-6 (two) and a timeout whose saved case is A.27 mutated so that it fails before clearing its globals (it hangs the same way before T1.5).
- Since T1.8 CRASH-6 is fixed and its entry is gone from `KnownFindings`. Run 2026-11-13 after T1.8 (3 jobs, `--seed 12345 --minutes 4`, 110 corpus files): 14,191 cases, 1 signature, none new: the BUDGET-3 timeout.
- Run 2026-11-06 on 3 cores (`--seed 20261107 --minutes 10`, 101 corpus files): 35,645 cases (54% compile errors, 19% ok, 27% findings), 12 signatures, all attributed: T1.3 (8,732 invariant violations after an unhandled error), CRASH-1, CRASH-3, and the new CRASH-5, CRASH-6, CRASH-7 and BUDGET-2. CRASH-2 needs a recursive constructor, which no mutation produced; A.3 covers it.
- Bytecode mode (T1.7): `--bytecode`. The corpus files that compile are compiled once in the fuzzer's process (known-good source, as the golden runner's bytecode mode does it: full stdlib, `SaveBytecode`); a case keeps the 9-byte header and sets 1 to 8 random later bytes to random values, or (one case in eight) truncates the file, and runs as a `.wsbc` file through `WarScript.Runner`, which loads it as it is (`GoldenFile.Bytecode`) and reports an `InvalidDataException` as status `rejected`, an acceptable outcome. Same budgets, timeout and signatures; output in `Headless~/out/fuzz-bytecode/`; `--case` needs `--bytecode` again, `--replay` takes the `.wsbc`. Imports are not compiled in, so an importing case fails to find its import. Most cases are rejected at load (a flipped byte in a name, count or line entry). Run 2026-11-13 on 3 cores (`--bytecode --seed 20261113 --minutes 5`, 108 compiled corpus files): 29,888 cases (27,793 rejected, 1,566 ok, 525 script errors), 2 signatures: the BUDGET-3 timeout (a mutated A.27) and one new, SEM-14 (Q-15): a `Return` turned into `YieldWait` in a method, so `Run()` ended with the method's scopes pushed; reproducible from source, recorded in 4.4 and attributed as `SEM-14?` in `KnownFindings`. No host fault. A 3-minute run with `--seed 12345` found none. Checked that the oracle works: with the verifier's `PopScope` check removed, a hand-made file with a stray `PopScope` is reported as a NEW host-fault signature.

### 9.7 Canonical state hash

`ComputeStateHash()` is a 64-bit FNV-1a hash over this byte stream:

1. For each printed line, in order: the line's UTF-16 code units (little-endian), then `0x0A 0x00`.
2. The byte `0xFF`.
3. For each user global of the instance, sorted by name with ordinal comparison: the name's UTF-16 code units (little-endian), then `0x00 0x00`, then the value's encoding. Host globals are excluded.

A value is encoded as one tag byte (`ValueTag`) followed by a payload:

| Type | Payload |
|---|---|
| null | Nothing |
| number | The 8-byte raw value, little-endian |
| logical | 1 byte |
| text | A 4-byte length, then the UTF-16 code units (little-endian) |
| array | A 4-byte element count, then each element's encoding. An array that is already being encoded is written as the cycle marker byte `0xFE`, and nesting beyond `MaxValueDepth` as `0xFD`. |
| instance | The class name, encoded as text, then the declared field values in field order. Cycles are handled as for arrays. |
| native object | The canonical formatting from 5.10, encoded as text |

Implementation notes (T0.6):

- Multi-byte values (lengths, counts, raw numbers) are little-endian, like the code units. A text payload, including a class name or a native object's formatting, is preceded by the text tag byte.
- Until T3.4 gives classes a declared field list, an instance is encoded as its class name, a 4-byte field count, the constructor parameters' values in declaration order, and then every other field of the instance sorted by name (ordinal), each as name, `0x00 0x00`, value. When T3.4 lands, switch to the declared field order above; that changes hashes of states that hold instances, so hashes recorded before it (the golden hash files, and the optional T0.9 recordings) must be re-recorded or compared by output only.
- Until T1.6, native objects were hashed through v1 formatting (the host `ToString()`), so a state holding an `F64Vec3` or another native object hashes differently before and after T1.6. Since T1.6 the hash uses the formatting without registered formatters: `F64Vec3` and function values by their built-in format, any other native object as `<TypeName>`.
- `MaxValueDepth` is 256 (PROP-5). Since T1.2 `StateHash.MaxValueDepth` is `WarValue.MaxValueDepth`, the limit equality and formatting use.
- The printed lines are those of the `print` builtin since the instance was created; error traces logged for unhandled errors are not included.

### 9.8 Cross-platform runs and IL2CPP

**With CI.** Run `dotnet test` on linux-x64, windows-x64 and macos-arm64, and upload the `golden-hashes.*.txt` files. A final job runs `compare-hashes.sh` over them.

**Without CI.** The owner runs the suite on the machines available, before releases.

**IL2CPP.** Periodically, and before releases, an IL2CPP player build of a small harness scene runs the golden corpus (copied into StreamingAssets) and writes hashes for comparison. Mono and IL2CPP use Unity's class libraries, which can differ from .NET in culture data and number formatting.

**Unity editor.** Keep running the EditMode tests in the Unity Test Runner as today.

### 9.9 Rules for tests

- Tests are deterministic: no wall-clock time, and no randomness without a recorded seed.
- Every finding ID has at least one test.
- Language behavior is tested with golden files by default. C# tests cover the host API, natives, allocation behavior and threading.
- Child-process and allocation tests carry `[Category("HeadlessOnly")]`.
- All test code stays within the NUnit 3.5 API so it also runs in Unity (R2).

---

## 10. Bytecode format and verifier

### 10.1 The opcode-info table

A single table drives the disassembler, the verifier and the format hash.

```csharp
internal enum OperandKind : byte
{
    U8Count,          // argument count (byte)
    U8Flag,           // 0 or 1
    U16Const,         // constant index, any tag
    U16Name,          // constant index that must refer to Text
    U16Local,         // local slot
    U16JumpForward,   // added to IP after the operands
    U16JumpBackward,  // subtracted from IP after the operands
    U16Cache,         // inline-cache slot
    U16Address,       // absolute code offset (exception-handler targets)
    U16Count          // element count (NewArray)
}

internal sealed class OpInfo
{
    public OpCode Op;
    public string Name;
    public OperandKind[] Operands;
    public int Pops, Pushes;          // fixed effects; -1 means computed by StackEffect(op, operands)
    public bool Terminates;           // Return, TailCall, Raise: no fall-through
    public bool UnconditionalJump;    // Jump, Loop: no fall-through
}
```

Opcodes with a variable stack effect:

| Opcode | Stack effect |
|---|---|
| `Call` | Pops `argc`, pushes 1 |
| `CallValue`, `CallMethod` | Pop `argc + 1`, push 1 |
| `NewInstance` | Pops `argc`, pushes 1 |
| `NewNestedInstance` | Pops `argc + 1`, pushes 1 |
| `NewArray` | Pops `n`, pushes 1 |
| `PopN` | Pops `n` |
| `JumpIfFalse`, `JumpIfTrue` | Peek; pop nothing |
| `LessJump` | Pops 2 |
| `PushHandler` | No effect, but records the handler's stack height |

### 10.2 File layout

The layout is little-endian throughout. Strings are a `u16` byte length followed by UTF-8 bytes.

```
Header      "WSBC" | version u8 | formatHash u32
Module      (v3 only) importCount u16, then importCount strings
Function    name string | arity u16 | localCount u16
            constCount u16 | constants (tag u8 + payload, as in v1)
            codeLength u32 (at most 65,535) | code bytes
            lineEntryCount u32 | entries: codeOffset u16, line i32 (v3 adds column u16);
                                 one entry for each point where the line changes
            localNameCount u16 | names
            cacheSlotCount u16
Definitions functions and classes, in the same structure as v1
```

`MaxStack` is not stored. The verifier computes it at load time, in linear time.

**Versions.** Version 2 is introduced by T1.7 and version 3 by T3.11. A change to the opcode table, an operand layout or this file layout requires a version bump. As implemented, T1.7 used two numbers: `BytecodeFormat.Version` 2 for its first slices (one `i32` line per code byte) 3 for the layout above with the run-length line table, and 4 once a rescue body ends with `PopHandler` (Q-14: the compiled code changed, the table and the file layout did not). The format this section calls v3 (T3.11) therefore gets `BytecodeFormat.Version` 5; "v2" and "v3" in this section and in T3.11 name the formats, not the header byte.

**Format hash.** `formatHash` is a 32-bit FNV-1a hash over, for each opcode in the `OpInfo` table: its numeric value, its name, its operand kinds and its stack effects; then the version number and a layout tag string. It is recorded as `BytecodeFormat.Hash` and checked by a test (T1.7).

### 10.3 Verifier checks

Every check below causes the file to be rejected with an `InvalidDataException`.

| Check | Rule |
|---|---|
| Header | Correct magic, exact version, exact format hash |
| Sizes | Every count fits in the remaining stream and within the limits: strings ≤ 65,535 bytes, code ≤ 65,535 bytes, constants ≤ 65,535, lambda-constant and class nesting ≤ 32 |
| Opcodes | Every byte at an instruction start is a defined opcode |
| Operands | The instruction's full length, taken from `OpInfo`, fits inside the code |
| Jumps | Targets lie in [0, codeLength] and on instruction boundaries. `Loop` jumps backward; forward jumps go forward. |
| Handlers | `PushHandler`'s rescue, ensure and end addresses are on instruction boundaries, after the `PushHandler`, and ordered rescue ≤ ensure ≤ end |
| Constants | Every index is below the constant count, and `U16Name` operands refer to Text constants |
| Locals | In v2, the slot of `GetLocal`, `SetLocal` and `IndexSetLocal` is below the current stack height relative to the frame base. In v3, it is below `localCount`. |
| Cache slots | Below `cacheSlotCount` |
| Stack heights | Abstract interpretation over the control-flow graph: the height is never negative, and it is consistent at every merge point. The rescue entry height is the handler height + 1 (the error value); the ensure entry height is the handler height. |
| MaxStack | The maximum height over the function (plus `localCount` in v3) is at most 1,024 |
| Termination | No path falls off the end of the code: every path ends in `Return`, `TailCall`, `Raise`, `Jump` or `Loop` |
| Metadata | In v3, `arity ≤ localCount` |

If verification finds an inconsistency in compiler output (for example mismatched heights at a merge point), that is a compiler bug. Fix the compiler; do not relax the verifier.

Added during T1.7 (the VM relies on both): the block scopes and handlers a frame has open are tracked like heights (never negative, consistent at merges, rescue and ensure entered at the `PushHandler`'s depths, none open at a `TailCall`), and a loaded function's arity is at most 255. Since Q-14 (version 4) the rescue body of a block with an ensure block (ensure address ≠ end address) is entered with one more handler open than the `PushHandler` had: the ensure-only handler it runs under, which its end removes. Without an ensure block it is entered at the `PushHandler`'s depth.

### 10.4 Loader hardening

- Read through a bounded reader that converts `EndOfStreamException` into `InvalidDataException("truncated bytecode at offset N")`.
- Check the length of the magic number before reading it.
- Before allocating a collection for a count, check that the count multiplied by the minimum element size fits in the bytes that remain.
- Limit the recursion depth for lambda constants and nested classes to 32.
- Decode UTF-8 with `new UTF8Encoding(false, true)`, which throws on invalid bytes.
- Verify every function before installing any definition, so a load either succeeds completely or changes nothing.

---

## 11. Deferred work

Nothing in this section is scheduled. The items are recorded so that later work starts from the data and the reasoning of the review.

### 11.1 Performance (Phase 4)

| ID | Item | Evidence and expected effect | Notes |
|---|---|---|---|
| F-PERF-1 | Call-site inline caches for `Call`, `TailCall` and `CallMethod` | Every call hashes a `(string, int)` key up the scope chain, and method calls walk the inheritance chain (`FindClassWithMethod`). Event glue consists mostly of calls, so this is the largest Phase 4 item. | Cache the resolved definition per call site and invalidate with the definitions version counter (T2.6). Keep the caches out of shared program data (F-VM-3). |
| F-PERF-2 | Zero-allocation native calls | Each native call allocates 80 B today, in `CollectArgs`. | Use `delegate WarValue NativeFn(ReadOnlySpan<WarValue> args)`. It must be a custom delegate type, because ref structs cannot be generic type arguments. Update `NativeHelper` and the template in `Editor/WsBindingGenerator.cs` (which emits the `new NativeFunctionDefinition(...)` registrations), then regenerate the bindings. This breaks C# natives, which is acceptable under R4. |
| F-PERF-3 | A concat-N opcode, and interning off the hot path | 20,000 interpolations with 3 holes allocate 7.5 MB. Interning a string after it has been allocated does not reduce allocation. | Desugar interpolation to one `ConcatN`, built with `string.Create` or a pooled `StringBuilder`. Remove `StringInterner` from the `Add`/`Sub`/`Mul` paths, or make it opt-in. |
| F-PERF-4 | Dispatch micro-optimizations | Measured: switching from `List<byte>` to `byte[]` made loops 10 to 14% faster, and gating the per-instruction checks behind one flag gained about 6% more. | Also keep the IP in a local, written back on call, return and throw; check the budget only on backward jumps and calls; use a separate debugger loop. `ExecuteLoop` is 12,306 bytes of IL with 230 locals. RyuJIT optimizes it, but Mono's JIT may not, so moving cold paths into helper methods can help there. |
| F-PERF-5 | Superinstructions chosen from measured opcode-pair frequencies | A simple counted loop executes 14 dispatches per iteration (Appendix B.3). | Add a debug opcode-pair histogram and run it over the core mod, skills and modifiers. Candidates: `SetLocal` + `Pop` becoming a store-local, `GetLocal` + `GetLocal` + `Add`, an increment-local, and a fused for-loop step. Each gets a golden disassembly test. These change when the budget trips (DEC-1 notes). |
| F-PERF-6 | A 16-byte `WarValue` | It is 24 bytes today: a 1-byte tag padded to 8, an `F64` and a reference. | Use `{ object Ref; long Raw; }`, with static sentinel objects as the tags for null, number and logical, so a tag test becomes a reference comparison. Only worth doing after F-PERF-1 to F-PERF-5. |
| F-PERF-7 | Slots for global variables | Globals are dictionary lookups by name. | Resolve module globals to indices when linking. |

### 11.2 Multiple VMs

| ID | Item | Evidence | Notes |
|---|---|---|---|
| F-VM-1 | Per-instance footprint | A trivial script costs 177.8 KB: 135.1 KB when the instance is constructed (mostly the `StringInterner` dictionary pre-sized to 4,096 entries), 12.7 KB to register the stdlib, and 30.0 KB on the first `Run`, including the 1,024-slot VM stack. | A lazy interner (or none, per F-PERF-3); a smaller initial stack that grows up to the limit; lazily created context objects; an immutable stdlib definition scope shared by all instances. |
| F-VM-2 | VM pooling | Nested entries (DEC-16), coroutines and one-shot instances each allocate a VM. | Start with a per-instance free list, and add a cross-instance pool only if profiling shows a need. |
| F-VM-3 | Shared compiled programs ("shared cores") | Many instances load the same scripts, and each one lexes, parses and compiles, or deserializes, separately. | Compile or deserialize once per script set and format version, then share the immutable program between instances. This requires inline caches and globals to be per instance; T3.4 keeps that possible. |
| F-VM-4 | Calls across instances | Under the Q-1 default, calling another instance's methods or functions raises an error. | If it is needed, add an explicit host-mediated call API that makes an outermost entry into the owning instance. |

### 11.3 Tooling

| ID | Item |
|---|---|
| F-TOOL-1 | A `wsc` command-line tool with check, compile, disassemble, run and report commands, built from the `Headless~` projects |
| F-TOOL-2 | A TextMate grammar for syntax highlighting in VS Code |
| F-TOOL-3 | A language server that reuses the compiler's diagnostics (T3.8) and the resolver's symbol tables for go-to-definition |
| F-TOOL-4 | Debugger improvements: locals read from frame slots, watch expressions, column-accurate breakpoints |

### 11.4 Language

These were not discussed in depth; they are possibilities to revisit.

| ID | Item |
|---|---|
| F-LANG-1 | Closures (upvalues). They become feasible once locals are stack slots (T3.5). |
| F-LANG-2 | Structured error objects for `rescue e`, carrying the kind, message, line and stack |
| F-LANG-3 | Numeric overflow detection. Today `F64` addition and subtraction wrap silently, multiplication overflow is unchecked, and division saturates. Decide whether overflow should raise an error. |
| F-LANG-4 | `@method [args]` as sugar for `this :: method [args]` |
| F-LANG-5 | An isolated host call that lets a native suppress a callback's error, if event dispatch needs it (6.5) |

### 11.5 Simulation

| ID | Item |
|---|---|
| F-SIM-1 | A command buffer for natives that mutate game state, applied only when the call returns `Ok`, so that a failed handler changes nothing (6.4) |

### 11.6 Testing

| ID | Item |
|---|---|
| F-TEST-1 | Coverage-guided fuzzing with SharpFuzz and libFuzzer or AFL |
| F-TEST-2 | Mutation testing with Stryker.NET to find weak tests |
| F-TEST-3 | Automated IL2CPP runs in CI, if a Unity build agent becomes available |

---

## 12. Open questions for the owner

Implementation proceeds with the default until the owner answers. The "Blocks" column shows which task needs the answer.

| ID | Question | Default until answered | Blocks |
|---|---|---|---|
| Q-1 | Do natives pass class instances, arrays or function values between instances (rooms, bootstrap instances)? | Plain data may cross instances, including reading and writing fields of a foreign instance. Calling a method or function value owned by another instance raises TypeError (6.7). | T3.5 (the ownership check) |
| Q-2 | When scripts are compiled for the mod archive, are the game's natives registered? | No longer blocks anything: the binding generator always emits the native manifest (PROP-30), so scripts can be checked either way. | Nothing |
| Q-3 | Are all instances driven from a single simulation thread? | Yes. An instance is not thread-safe, and the runtime has no unsafe shared static state (T1.9). | Nothing |
| Q-4 | When an instance halts, what should its room or map do? | Host policy, outside this plan. The API provides `Faulted`, `Fault` and `InstanceLabel`. | Nothing in this plan |
| Q-5 | How are script state and halted flags handled on save, load and rejoin? | The halted flag must be restored along with whatever other state is restored. If scripts are re-run on load, replay must reproduce the halt deterministically. | Nothing now |
| Q-6 | Are the default limits in PROP-5 acceptable? | Use the values as listed | T1.4 |
| Q-7 | Do any game scripts rely on dynamic properties, text index assignment, structural class equality, or methods reading null where a global exists? | Not asked any more (DEC-20). The migration tool report and the v2 compiler (PROP-28, PROP-29) find the static cases; the rest raise v2 errors at runtime. | Nothing |
| Q-8 | Is the newest-first coroutine resume order intended? | Keep it (PROP-24) | Nothing |
| Q-9 | Should `Call` be allowed before `Run`? | Allowed, as in v1 (PROP-25) | Nothing |
| Q-10 | Where are the game's `.ws` scripts, for the migration? | The owner provides paths, or runs the migration tool and the compiler on the owner's side | T3.9 |
| Q-11 | The fuzzer (T0.7) found CRASH-5, CRASH-6, CRASH-7 and BUDGET-2. Which tasks fix them, and: should a class inheriting from itself be a compile error or a runtime error at `new` (CRASH-5)? After the instruction budget runs out, should it stay exhausted, so that the error ends the entry even if the script rescues it (BUDGET-2)? | CRASH-5 in T1.1 (its allocation step walks the base classes and must detect the cycle), as a rescuable error at `new`; CRASH-6 in T1.8 (compile errors); CRASH-7 in T1.3; BUDGET-2 in T1.5 (instance-level budgets), with the budget staying exhausted. Goldens A.22 to A.25 are written against these defaults. | T1.1, T1.3, T1.5, T1.8 |
| Q-12 | The fuzzer found CRASH-8 during T1.2: `break`/`next` inside `begin` leaves the handler behind. Which task fixes it, and should leaving a `begin` block by `break` or `next` run its `ensure` block (v1 skips it)? | A follow-up to T1.3 (the exception boundary): `break` and `next` remove the handlers of the `begin` blocks they leave and run their `ensure` blocks, as `return` already does. Golden A.26 is written against this default. | T1.3 follow-up |
| Q-13 | The state hash (9.7) walks shared arrays once per path, like equality did before the BUDGET-3 fix, so `ComputeStateHash()` does not return for a global holding 40 levels of `a = {a, a}`. Fixing it changes the pinned encoding for every value that holds the same array twice (for example `{s, s}`), which invalidates recorded hashes. Should an array or instance already encoded be written as a back-reference marker with its index? | Yes, with a new marker byte 0xFC followed by a 4-byte index in encoding order; done together with the next change that invalidates the hashes anyway (T1.6 changes number formatting). Until then golden files must not leave such values in globals (A.27 clears them). | T1.6 |
| Q-14 | Leaving a rescue body by `return`, `raise` or a runtime error skips the `ensure` block (SEM-13), while `break`/`next` out of it run it (Q-12). Section 5.9 says ensure semantics are unchanged; should every way out of a rescue body run the ensure block? | Yes: when the rescue body is entered with an ensure block, the VM keeps an ensure-only handler for it until the rescue body ends. This needs a compiler change too (the rescue body's normal exit and a `break`/`next` out of it remove that handler), so bytecode compiled before it would leave the handler behind; land it with the format change in T1.7. Golden A.28 is written against this default. Done in T1.7 as proposed (version 4). | T1.7 (done) |
| Q-15 | A `yield` reached outside a coroutine in a function, method or constructor called from the script body ends `Run()` with that frame's contexts still pushed and the rest of the script not run (SEM-14). What should it do? | Proposed: a rescuable error "yield outside a coroutine" at the `yield` when the VM is not running a coroutine and the frame is not the entry's own script body (a top-level `yield` keeps its v1 meaning); a compile error where the compiler can prove it (DEC-21). Not implemented. | — |

---

## Appendix A. Repro corpus

Each repro becomes a golden file or a C# test in T0.5, marked `# known-failing:` until its fix lands. "v1" is the observed behavior; "v2" is the expected behavior after the named task.

### A.1 Formatting a cyclic array (CRASH-1, T1.2)

```ruby
# isolate
a = {1}
a << a
print a           # expect: [1, [...]]
```

v1 aborts the process with a stack overflow in `WarValue.ToString()`.

### A.2 Comparing cyclic arrays (CRASH-1, T1.2)

```ruby
# isolate
a = {1}
a << a
b = {1}
b << b
begin
    print a == b
rescue e
    print "rescued"   # expect: rescued
end
```

v1 aborts the process with a stack overflow in `WarValue.Equals`. v2 raises LimitExceeded, which the script rescues.

### A.3 Recursive constructor (CRASH-2, T1.1)

```ruby
# isolate
# budget: 100000
class Node [v]
    child = new Node [v + 1]
end
begin
    n = new Node [0]
rescue e
    print "rescued " + e      # expect: rescued Stack overflow
end
```

v1 aborts the process even with the budget set. v2 raises LimitExceeded once the 128-frame limit is reached, which the script rescues.

### A.4 Subtracting an empty text (CRASH-3, T1.4, then T3.6)

```ruby
x = "abc" - ""
print x
```

| Version | Behavior |
|---|---|
| v1 | `ArgumentException` escapes `Run()` |
| After Phase 1 | Prints `abc` |
| After Phase 3 | `# expect-error: TypeError`, because the operator is removed; use `Text_remove` |

### A.5 Huge text repetition (CRASH-3, T1.4)

```ruby
begin
    x = "ab" * 2000000000
rescue e
    print "rescued"   # expect: rescued
end
```

v1: `ArgumentOutOfRangeException` escapes. v2: LimitExceeded "Text too long", which the script rescues.

### A.6 Re-entrant host call (CRASH-4, T1.5)

This is a C# test, because it needs a native function.

```csharp
WarScriptLanguage s = null;
var output = new List<string>();
s = new WarScriptLanguage("t", @"
fun on_event [x]
    return x
end
fun main []
    a = 1
    b = fire []
    print ""after fire, a="" + a + "" b="" + b
    return 0
end
main []
print ""top done""", null, (w, m) => output.Add(m));
s.GlobalDefinitionScope.AddFunction(new NativeFunctionDefinition(
    new FunctionDetails("fire", new List<string>()),
    args => {
        s.Call(s.GetFunction("on_event", 1), WarValue.FromNumeric(42));
        // From Phase 2: assert the result is Ok and its value equals 42.
        return WarValue.FromNumeric(7);
    }, "", ""));
s.Run();
// Expected: output == ["after fire, a=1 b=7", "top done"], no exception escapes, invariants hold.
```

v1 prints `after fire, a=1 b=7`, then an `IndexOutOfRangeException` escapes to the host. After Phase 3, the script itself is unchanged, because `a` and `b` are function locals in both versions.

Also add these variants:

- a callback that raises, with `begin/rescue` around `fire []` in the outer script, so the error is rescued there (PROP-1);
- the same callback without the rescue, where the outer call ends with the error;
- a native and a script function that call each other until `MaxHostNesting` stops them.

### A.7 Budget bypass through constructors (BUDGET-1, T1.1 and T1.5)

```ruby
# budget: 5000
# expect-error: InstructionBudgetExceeded
class Burn [n]
    i = 0
    loop i < 300
        i += 1
    end
end
loop j in 0..200
    b = new Burn [0]
end
print "done"
```

In v1 the budget is bypassed and the script prints `done`, while the same work written inline trips the budget. In v2 this script trips the budget.

### A.8 Culture-dependent formatting (DET-1, T1.6)

This runs in a C# test under each culture in 9.4.

```ruby
x = 0 - 1500
print "hp:" + x   # expect: hp:-1500   (ASCII U+002D in every culture)
```

v1 prints U+2212 under sv-SE, nb-NO and fi-FI.

### A.9 Formatting fractions and native objects (DET-2, DET-3, T1.6)

This is a C# test. The host passes `new F64Vec3(F64.FromInt(1), F64.Ratio(1, 3), F64.FromInt(-2))` to a script that prints it.

v1 prints `(1, 0.3333333332557231, -2)`, formatted through `double`. v2 prints `(1, 0.33333333325572311878204345703125, -2)`. No decimal with 9 or fewer digits round-trips to raw value 1431655765 through the truncating literal parser, so the exact 32-digit expansion is used.

Also cover `0.5`, `-0.5`, `99.5`, `3.14`, `0.7`, `F64.MinValue`, `F64.MaxValue` and `F64.FromRaw(1)` in every culture.

### A.10 Function writing a global (SEM-1, DEC-5)

```ruby
count = 10
fun helper []
    count = 0
    loop i in 0..3
        count += 1
    end
    return count
end
print helper []   # expect: 3
print count       # v1: 3. v2 as written: 10. v2 as migrated (with "global count"): 3.
```

### A.11 A typo creates a variable (SEM-2, PROP-26)

```ruby
health = 100
fun damage [amt]
    helth = health - amt      # expect-warning: local 'helth' is assigned but never read
    return health
end
print damage [30]             # expect: 100
```

### A.12 Coercions (SEM-6, SEM-11, DEC-8)

Each row is a separate golden file.

| Expression | v1 result | v2 result |
|---|---|---|
| `1 == "1"` | True | `false` |
| `null == 0` | False | `false` |
| `"10" < 9` | True | TypeError |
| `10 < "9"` | True | TypeError |
| `true < false` | False | TypeError |
| `-"abc"` | 0 | TypeError |
| `true + 1` | `True1` | TypeError |
| `5 - "5"` | `""` | TypeError |
| `{1, 2} + 5` | `[1, 2, 5]` | TypeError |
| `5 << 3` | 5 | TypeError |
| `"n=" + 5` | `n=5` | `n=5` (unchanged) |
| `"b=" + true` | `b=True` | `b=true` |

### A.13 Silent failures (SEM-7, DEC-10)

| Code | v1 | v2 |
|---|---|---|
| `arr = {1, 2, 3}` then `arr{10} = 99` | Ignored | IndexError |
| `arr{10}` | null | IndexError |
| `arr{-1}` | null | IndexError |
| `arr{"a"}` | null | TypeError |
| `x = null` then `x :: hp = 5` | Ignored | TypeError |
| `s = "abc"` then `s{0} = "x"` | `s` becomes `"xbc"` | TypeError |

### A.14 Class equality (SEM-8, DEC-9, PROP-8)

```ruby
class P [x]
end
print (new P [1]) == (new P [1])   # v1: True. v2: false
p = new P [1]
print p == p                        # expect: true
```

Also add an inheritance case: for a derived instance `d`, `d == (d as Base)` is `true` in v2.

### A.15 Boolean formatting (SEM-9, DEC-11)

```ruby
print true            # v1: True. v2: true
print "f: " + false   # v1: f: False. v2: f: false
```

### A.16 Globals inside methods (SEM-4, PROP-20)

```ruby
g = 5
class C
    fun m []
        return g
    end
end
print (new C) :: m []   # v1: null. v2: 5
```

### A.17 Top-level block scope (SEM-5, DEC-4)

```ruby
if true
    made = 1
end
print made    # v1: null. v2: 1
```

### A.18 Fields in methods (DEC-6)

The before-and-after example in 8.4 becomes a golden file in its v2 form. Add these compile-error goldens:

- `@nope` where `nope` is not a field;
- `@x` inside a lambda;
- `global x` at top level;
- reading a local before its first assignment;
- a loop variable that shadows a parameter.

### A.19 Disassembler (DX-5, T1.7)

This is a C# test: compile the following function and disassemble every compiled function.

```ruby
fun f [arr, i]
    begin
        arr{i} = 1
    rescue e
        print e
    end
    return arr
end
```

In v1, `Chunk.Disassemble` throws `ArgumentOutOfRangeException`. In v2 it succeeds, and the instruction boundaries it reports equal the verifier's.

### A.20 Malformed bytecode (SEC-1, T1.7)

These are C# tests. Each case must be rejected at load time with `InvalidDataException`, and none may execute anything or crash:

- a file truncated right after the header;
- the wrong version;
- the wrong format hash;
- a jump into the middle of an instruction;
- a constant index out of range;
- a `U16Name` operand pointing at a numeric constant;
- a `GetLocal` slot at or above the stack height;
- an unknown opcode byte;
- lambda constants nested 1,000 levels deep;
- a code path that falls off the end of the code.

### A.21 A native method that throws (CRASH-3, T1.3)

This is a C# test. Register a class whose method is a native that throws `InvalidOperationException`. Calling it from a script inside `begin/rescue` must be rescued as NativeError, and the invariants must hold. In v1 the exception escapes the VM, because `CallMethod`'s native path has no try/catch.

### A.22 A class that inherits from itself (CRASH-5, found by T0.7)

`Tests/Golden~/repro/A22_cyclic_inheritance.ws` (`# isolate`):

```ruby
class A
end
class A : A
end
begin
    a = new A
rescue e
    print "rescued"   # expect: rescued
end
```

v1 aborts the process with a stack overflow in `InstantiateClassDefShared`. The v2 outcome is the Q-11 default.

### A.23 C# exceptions from the parser and compiler (CRASH-6, found by T0.7)

`A23a_parser_exception_operator.ws`: `x = (1]   # expect-compile-error:`. v1 throws `System.Exception` from `OperatorExtension.ToBinaryExpression`.

`A23b_compiler_exception_cast.ws`: `x = p as "R" :: n   # expect-compile-error:` with `p` an instance. v1 throws `InvalidCastException` from `Compiler.CompileExpression`.

### A.24 Raising inside `ensure` (CRASH-7, found by T0.7)

```ruby
begin
    begin
        raise "first"
    ensure
        raise "second"
    end
rescue e
    print "rescued " + e   # expect: rescued second
end
```

v1 does not run the rescue, and `Run()` throws `NullReferenceException` from `ExceptionContext.PrintStackTrace`.

`A24b_return_in_ensure.ws` covers `return` out of `ensure`:

```ruby
fun f []
    begin
        raise "first"
    ensure
        return 1
    end
end
begin
    print f []              # expect: 1
rescue e
    print "rescued " + e
end
print "after"               # expect: after
```

v1 prints `1`, then "first" escapes unhandled and `after` is not printed. The `return` discards the error, as v1 already does when nothing rescues around the call (it then prints `1` and `after`, but leaves the VM non-idle).

### A.25 Rescuing the instruction budget (BUDGET-2, found by T0.7)

```ruby
# budget: 1000
begin
    loop true
    end
rescue e
end
n = 0
loop i in 0..100000
    n += 1
end
print n
# expect-error: InstructionBudgetExceeded
```

v1 prints `100000`: the budget is disarmed once it has run out. With an infinite loop in place of the counted one, v1 never returns. Since T1.5 the budget stays exhausted until the next outermost entry: every instruction after the error raises it again, so rescue and ensure bodies do not run and the error ends the entry.

### A.26 Leaving a `begin` block by `break` (CRASH-8, found during T1.2)

```ruby
# budget: 10000
loop true
    if true
        begin
            break
        rescue e
            print "stale rescue"
        end
    end
end
assert false
# expect-error: AssertionFailed
```

v1 prints `stale rescue` twice and ends in a host fault ("Stack empty" from `MemoryContext.EndScope`): the handler of the `begin` block is still on the VM after `break`, so it catches the failed assertion. v2 removes the handler when the loop is left, so the assertion is unhandled and nothing is printed.

### A.27 Shared arrays (BUDGET-3, found reviewing T1.2)

```ruby
# budget: 100000
a = {1}
b = {1}
loop i in 0..40
    a = {a, a}
    b = {b, b}
end
print a == b
# expect: True
```

Before the fix, `a == b` compares 2^40 leaf pairs and never returns, and so does `print a`. The golden (`repro/A27_shared_arrays.ws`) also checks `!=`, `Array_contains`, an unequal pair, and that the memo keeps the depth limit; `Tests/ValueDepthTests.cs` checks the formatting cap.

### A.28 Leaving a rescue body (SEM-13, found reviewing the CRASH-8 fix)

```ruby
fun leave_by_return []
    begin
        raise "a"
    rescue e
        return "returned"
    ensure
        print "ensure after return"
    end
end
print leave_by_return []
# expect: ensure after return
# expect: returned
```

v1 prints only `returned`: the handler that would run the ensure block is removed when the rescue body is entered. Fixed by T1.7 (Q-14): the rescue body runs under an ensure-only handler. The golden (`repro/A28_leave_rescue_body.ws`) also leaves the rescue body by `raise` and by a division by zero.

---

## Appendix B. Benchmarks and measurements

Environment: .NET 8.0, Release build, Linux x64, RyuJIT, best of 5 runs after a warm-up. These numbers are not representative of Mono or IL2CPP in absolute terms.

### B.1 Baseline

| Benchmark | Time | Allocated |
|---|---|---|
| `fib(22)`, recursive (57,313 calls) | 15 ms (about 260 ns per call) | 0.04 MB |
| 200,000-iteration loop, parameters only | 28 to 31 ms (about 140 ns per iteration) | 0.03 MB |
| The same loop with a local in the body | 40 ms | 0.03 MB |
| The same loop at top level, using globals | 40 ms | 0.03 MB |
| 200,000 native calls (`abs`) | 76 ms | 15.3 MB |
| 200,000 method calls | 131 ms | 0.04 MB |
| 20,000 string interpolations with 3 holes | 19 ms | 7.5 MB |
| 20,000 `new` with a constructor body | 84 ms | 565 MB |
| 20,000 `new` without a constructor body | 17 ms | 14.4 MB |

The scripts:

```ruby
# fib
fun fib [n]
 if n < 2
  return n
 end
 return fib [n - 1] + fib [n - 2]
end
x = fib [22]

# loop, parameters only
fun work [s]
 loop i in 0..200000
  s = s + i
 end
 return s
end
x = work [0]

# loop, body local
fun work []
 s = 0
 loop i in 0..200000
  s = s + i
 end
 return s
end
x = work []

# native calls (stdlib registered)
fun work [s]
 loop i in 0..200000
  s = s + abs [i]
 end
 return s
end
x = work [0]

# method calls
class C [v]
 fun bump [d]
  this :: v = this :: v + d
  return 0
 end
end
fun work [c]
 loop i in 0..200000
  c :: bump [1]
 end
 return 0
end
x = work [new C [0]]

# interpolation
fun work [s]
 loop i in 0..20000
  s = "unit {i} hp {i} of {i}"
 end
 return s
end
x = work [""]

# instances with a constructor body (remove "z = 0" for the variant without one)
class V [x, y]
 z = 0
end
fun work [s]
 loop i in 0..20000
  s = new V [i, i]
 end
 return s
end
x = work [0]
```

### B.2 Dispatch experiments (on a copy of the runtime)

| Variant | Loop, parameters only | Loop, body local |
|---|---|---|
| Baseline | 30.6 ms | 40.2 ms |
| `List<byte>` → `byte[]` for code | 26.4 ms | 36.2 ms |
| Also gating the budget, debugger and pending checks behind one flag | 24.9 ms | 35.7 ms |

`ExecuteLoop` is 12,306 bytes of IL with 230 locals. RyuJIT compiles it first at instrumented tier 0 and then as Tier1-OSR with dynamic PGO, so it is fully optimized under .NET. With tiered compilation disabled, the baseline loops run in 24.6 ms and 31.8 ms.

### B.3 Disassembly of the parameters-only loop

There are 14 dispatches per iteration, at instructions 0009 to 0040:

```
0000    0 CONSTANT        0 (0)
0003    | CONSTANT        1 (200000)
0006    | CONSTANT        2 (1)
0009    2 GetLocal        1
0012    | GetLocal        2
0015    | LessJump        15 -> 43
0018    0 GetLocal        0
0021    | GetLocal        1
0024    | Add
0025    3 SetLocal        0
0028    | Pop
0029    2 GetLocal        1
0032    | GetLocal        3
0035    | Add
0036    | SetLocal        1
0039    | Pop
0040    | Loop            40 -> 9
0043    | POP_N           3
0045    0 GetLocal        0
0048    5 Return
```

Note the line-0 entries (DX-3).

The same function with a body local compiles that local by name:

```
0000    0 CONSTANT        0 (0)
0003    8 SetGlobal       1 (t)
0006    | Pop
0007    0 GetGlobal       1 (t)
...
```

### B.4 Cost per instance

For `new WarScriptLanguage("one", "x = 1", null, null)`, then registering the stdlib, then `Run()`:

| Step | Allocated |
|---|---|
| Construction | 135.1 KB |
| Registering the stdlib | 12.7 KB |
| First `Run`, including compilation and the VM | 30.0 KB |
| Total | 177.8 KB |

---

## Appendix C. Unity `.meta` templates

Generate a GUID with `python3 -c "import uuid; print(uuid.uuid4().hex)"`. Never copy an existing GUID. Files inside `~` folders need no `.meta`.

**C# source file.** This is the minimal form the repository already uses:

```yaml
fileFormatVersion: 2
guid: <32 hex characters>
```

**Folder:**

```yaml
fileFormatVersion: 2
guid: <32 hex characters>
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

**Assembly definition (`.asmdef`):**

```yaml
fileFormatVersion: 2
guid: <32 hex characters>
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

**Other files** (`.ws`, `.md` and the like) outside `~` folders:

```yaml
fileFormatVersion: 2
guid: <32 hex characters>
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

---

## Appendix D. File map

| Path | Phases | What happens |
|---|---|---|
| `Runtime/Bytecode/WarVM.cs` | 1, 2, 3 | Constructor frames; exception boundary; re-entrancy and budgets; `MaxStack` checks; `default` opcode case; structured errors; semantic checks; new frame layout; scope removal |
| `Runtime/Bytecode/Compiler.cs` | 1, 3 | Limit checks; line numbers; resolver pre-pass; slot locals; `@x`; fields; static imports; diagnostics |
| `Runtime/Bytecode/Chunk.cs` | 1 | Disassembler driven by `OpInfo`; run-length line table |
| `Runtime/Bytecode/OpCode.cs` | 1, 3 | Explicit values; unused opcodes removed; Phase 3 opcode changes |
| `Runtime/Bytecode/BytecodeSerializer.cs` | 1, 3 | Formats v2 and v3; hardening; verification before install |
| `Runtime/Bytecode/BytecodeVerifier.cs` (new) | 1, 3 | Verifier (10.3) |
| `Runtime/Bytecode/OpInfo.cs` (new) | 1 | Opcode-info table and format hash (10.1) |
| `Runtime/Bytecode/CompiledFunction.cs` | 1, 3 | `FrameKind`; `MaxStack`; `this` held in the frame |
| `Runtime/Coroutine/BytecodeCoroutine.cs` | 1, 2, 3 | Shared budgets; fault reporting; simpler suspend and resume |
| `Runtime/Coroutine/Coroutine.cs` | 2 | Deleted |
| `Runtime/Expression/Value/WarValue.cs` | 1, 3 | Cycle-safe formatting, equality and hashing; integer-only formatting; native formatting; v2 semantics |
| `Runtime/Expression/Value/ClassData.cs` | 1, 3 | Depth guard; then structural equality removed; root instance for identity; owner reference (Q-1) |
| `Runtime/Context/MemoryScope.cs`, `MemoryContext.cs` | 0, 3 | Scope kinds for the audit (temporary); reduced to one globals store in Phase 3 |
| `Runtime/Context/Break*`, `Next*`, `Return*` | 2 | Deleted |
| `Runtime/Context/ExceptionContext.cs`, `Runtime/Exception/*` | 2 | Replaced by the `ScriptError` model |
| `Runtime/Context/Definition/DefinitionScope.cs` | 2, 3 | Definitions version counter; duplicate detection |
| `Runtime/WarScriptLanguage.cs` | 0, 1, 2 | `ComputeStateHash`; audit sink; limits; budgets; the v2 API; tree-walk removal |
| `Runtime/Parser/LexicalParser.cs` | 1, 3 | Bounded, locked cache; columns; `@`; the `global` keyword |
| `Runtime/Parser/StatementParser.cs`, `Runtime/Expression/ExpressionReader.cs` | 3 | `global`; import placement; module structure |
| `Runtime/Parser/NumericLiteral.cs` | 1 | Non-throwing `TryParseRaw` |
| `Runtime/Statement/*`, `Runtime/Expression/*` | 2 | `Execute`/`Evaluate` removed; optional consolidation of operator nodes |
| `Runtime/Native/NativeHelper.cs` | 2 | Throws `ScriptErrorException` |
| `Runtime/Native/Libraries/*` | 1, 3 | Size guards; new `Text_remove` |
| `Runtime/AssemblyInfo.cs` (new) | 0 | `InternalsVisibleTo("WarScript.Tests")` |
| `Editor/WsBindingGenerator.cs` | 3, deferred | Native manifest (PROP-30, T3.8); native ABI (F-PERF-2, deferred) |
| `Tests/*` | 0 to 3 | Invariant checks; golden runner; host-API tests; migration of embedded scripts |
| `Tests/Golden~/` (new) | 0 to 3 | Golden corpus |
| `Headless~/` (new) | 0 | Projects, runner, fuzzer, hash comparison |
| `Tools~/WarScript.Migrate/` (new, temporary) | 3 | Migration tool |
| `Documentation~/` (new) | 0 to 3 | This plan, migration reports, `LANGUAGE.md` |
| `CLAUDE.md`, `README.md` | Every phase | Kept current (R7) |

---

## Appendix E. Task tracker

| ID | Task | Depends on | Status | Notes |
|---|---|---|---|---|
| T0.1 | Headless build and test projects | — | done | `e2dbd4e`. 872 tests pass headless. |
| T0.2 | Golden script runner | T0.1 | done | `6d34caa`. 69 assert-only resources moved to `Tests/Golden~/`; scripts still driven by C# tests (`RunFile`, benchmarks) stay in `Tests/resources/`. |
| T0.3 | Invariant checks | T0.1 | done | `3c6d086`. 15 tests carry `[KnownInvariantViolation("T1.3")]`. |
| T0.4 | Bytecode round-trip and culture matrix | T0.2 | done | `6d34caa`. DET-1 shows as known failures under sv-SE in three goldens and A.8. |
| T0.5 | Known-bug corpus and child-process runner | T0.2 | done | Runner in `Headless~/Runner/`; `# isolate` goldens run in child processes (A.1 to A.3 contained). C# repros A.6 (with its three variants), A.9, A.19 and A.21 in `Tests/ReproTests.cs`. A.20 moves to T1.7 (its cases are defined against format v2: format hash, verifier) and A.18 to T3.4 (v2 syntax: `@x`, `global`). |
| T0.6 | State hash | T0.2 | done | `WarScriptLanguage.ComputeStateHash()` (`Runtime/StateHash.cs`); the golden runner requires the same hash in both modes and every culture and writes `<file> [<mode>]: <hash>` lines to `Headless~/out/golden-hashes.<os>-<arch>.txt`; `Headless~/compare-hashes.sh` diffs them. Encoding notes in 9.7. Cross-machine comparison needs CI or the owner (9.8). |
| T0.7 | Fuzzer v0 | T0.5 | done | `Headless~/Fuzz/` (notes in 9.6). A 10-minute run attributes every finding to a known ID. New findings reported to the owner: CRASH-5, CRASH-6, CRASH-7, BUDGET-2 (section 4, goldens A.22 to A.25, Q-11). The runner and `GoldenRun` now name the WarScript method a crash or escaped exception happened in. Bytecode mode waits for T1.7. |
| T0.8 | Audit mode | T0.1 | done | `WarScriptLanguage.AuditSink`, `AuditEvent`/`AuditKind`, `AuditReport` (`Runtime/Audit/`); all 20 events of 8.1 (notes there). Test-suite report: `Documentation~/migration/audit-tests.md` (`WARSCRIPT_AUDIT_REPORT=<file> dotnet test`). Owner instructions for game sessions: `Documentation~/migration/AUDIT.md`. The game-session owner step was dropped on 2026-11-12 (DEC-20). |
| T0.9 | Behavior baseline (owner) | T0.6 | dropped | `Documentation~/migration/BASELINE.md` written. run-1 (2026-11-06) reviewed there and not accepted: every recorded hash is the "no user globals" value, so it compares only three printed lines; no repeat run, no README. The recorder now also writes `<label>.globals.txt`. The full warman mod (2026-11-06) has no assignments at all, which explains run-1; its script behavior is baselined headlessly as native-call traces (`Headless~/ModTrace`, hashes in BASELINE.md). Dropped on 2026-11-12 (DEC-20): no game baseline gates Phase 3; BASELINE.md and ModTrace stay as optional tools. |
| T1.1 | Constructors as frames | Phase 0 | done | `new` is an allocation step (`WarVM.AllocateInstance`: the derived and base-class `ClassData`, shared `ValueReference`s and property values, no code run; it queues the constructor bodies in v1 order, bases depth-first in `BaseTypes` order, then the derived class) followed by `EnterConstructors`, which pushes the instance as the expression's value and one `Constructor` frame per body on the current VM, in reverse execution order; it raises "Stack overflow" before pushing anything when the frames do not all fit. `CallFrame.IsMethodCall`/`HasScope` became `FrameKind` (`Script`, `Function`, `Method`, `Constructor`; `Function` is needed besides the plan's three because entry frames push no scope) and constructor frames record their `ClassData` (`CallFrame.Instance`); `PopFrameContexts` pops what each kind pushed, for `DoReturn` and unwinding alike, and a constructor frame's return value is discarded. CRASH-5 (Q-11 default): the allocation step tracks the base-class path and raises the rescuable "Class 'X' inherits from itself". A `TailCall` in a constructor frame (never emitted by the compiler) raises an error instead of reusing the frame. Behaviour change: a `yield` inside a constructor body now suspends the caller (a coroutine keeps the constructor's contexts across ticks like any other frame) instead of ending only the constructor's private VM; outside a coroutine, `Run()` then stops with the constructor's contexts still pushed (an invariant violation), exactly as v1 already did for a `yield` inside a method called from the script body. PROP-21 makes `yield` in a class body a compile error in Phase 3; the method case is open (not changed here). A.3, A.7 and A.22 pass (A.3 and A.22 stay `# isolate` as a guard); BUDGET-1 is fixed for constructors and remains open for imports (T1.5). New golden `language/constructor_frames.ws` and `Tests/ConstructorFrameTests.cs` (720 bytes per instance in the 20,000-instance allocation test, against about 29 KB before; budgets charged; frame overflow before any constructor runs). The fuzzer's CRASH-2 and CRASH-5 entries are removed. |
| T1.2 | Cycle-safe value operations | Phase 0 | done | `WarValue.ToString` formats arrays through a recursive formatter that carries the arrays on the current path and the depth: a cycle or anything nested deeper than `WarValue.MaxValueDepth` (256, shared with `StateHash`) prints `[...]`. `WarValue.Equals` and `ClassData.StructuralEquals` carry a depth and throw `WarScriptLimitException` "Value nested too deeply" beyond it, which T1.3 turns into a rescuable LimitExceeded (in `==`/`!=` and the natives alike); `GetHashCode`/`StructuralHashCode` hash the count plus at most 8 elements or properties, 3 levels deep. No static state (R1). A.1 and A.2 pass; new golden `language/value_depth.ws`, `Tests/ValueDepthTests.cs`; the fuzzer's CRASH-1 entry removed. The T1.2 fuzz run found CRASH-8 (A.26, Q-12), recorded and not fixed here. Review: BUDGET-3 (A.27) fixed for equality (memo of equal pairs) and formatting (`WarValue.MaxFormattedLength`); the state hash part is open (Q-13). |
| T1.3 | Exception boundary and context restoration | Phase 0 | done | `WarVM.Execute` is the boundary: `ScriptErrorException` (new, with `ScriptErrorKind`; `WarScriptLimitException` derives from it) and value-stack overflow become rescuable script errors; an unhandled error unwinds every frame (`UnwindAll`); any other exception restores the context depths recorded at the VM entry, resets the VM and propagates to the host entry (`Run`, `Call`, coroutine resume), which logs it with its C# stack trace and records `LastHostFault` (internal). Exceptions from native methods (`CallMethod`) become NativeError like those of functions. A `SyntaxException` from compiling an import still reaches the host, after the same cleanup. CRASH-7 fixed: the single pending-exception/pending-return slots became a stack of pending ensure records, discarded when a `return`, an error caught outside the block, or unwinding leaves the block. All 15 `[KnownInvariantViolation("T1.3")]` markers, A.21 and A.24 removed from the known failures; new `Tests/ExceptionBoundaryTests.cs`, golden `language/ensure_leaving.ws`. Host faults are triggered in tests through a throwing `DebugHook`. Follow-up after T1.2 (Q-12): CRASH-8 and SEM-12 fixed; `break`/`next` remove the handlers of the `begin` blocks they leave, discard their pending re-raise or return (new `DropPending` opcode, appended) and run their ensure blocks; A.26 passes, golden `language/break_out_of_begin.ws`. |
| T1.4 | Allocation guards | T1.3 | done | `WarScriptLanguage.MaxTextLength` and `MaxArrayLength` (PROP-5 defaults, 1,048,576). The VM checks the resulting size in `long` arithmetic before allocating: text concatenation, repetition (a count of 0 or less gives `""`) and index writes (`s{i} = t` can grow a text) raise "Text too long"; array `+`, `<<`, array literals and `Array_insert` raise "Array too long" (`Array_insert` throws `WarScriptLimitException`, which T1.3 turns into the script error). Both are LimitExceeded for the golden runner. Every `TrackAlloc` site now checks the result before allocating and raises "Memory budget exceeded" at once, so a failed `<<` leaves the array unchanged; text subtraction is the one exception, tracked after the replace, because its result is never longer than its left operand. `text - ""` returns the left text (VM and tree-walker). A.4 and A.5 pass; new `Tests/AllocationGuardTests.cs` (at the limit / one past, budget checked before a 2,000,000,000-character repeat), golden `language/text_length_limit.ws`; the fuzzer's CRASH-3 entries removed. |
| T1.5 | Re-entrancy and instance budgets | T1.1, T1.3 | done | The budgets moved from `WarVM` into `ExecutionBudget` (`WarScriptLanguage.Budget`), shared by every VM of the instance; `BeginEntry`/`EndEntry` around `Run`, `Call` (bytecode path) and `BytecodeCoroutine.Resume` count `EntryDepth`, reset the budget only at the outermost entry and refuse an entry nested deeper than `MaxHostNesting` (32, a public const; outermost + 32 nested) with the rescuable "Host nesting limit exceeded" (LimitExceeded), raised for the native that made the call. A nested entry whose cached VM is busy runs on a new `WarVM` (`AcquireVM`); `WarVM.IsRunning` guards `Reset()` and `Execute()` against re-entry, and a coroutine resumed from inside itself is skipped. PROP-1: a nested entry leaves its unhandled error raised (only the outermost `EndEntry` logs it), so it continues from the native call site. Not changed: a coroutine's own unhandled error is still logged and cleared even when a script started it, a host fault inside a nested entry is reported by that entry (the native then continues; T2.4 decides), and the tree-walk `Call` fallback is not an entry. A.6 and its three variants pass; new `Tests/ReentrancyTests.cs` (callback instructions and allocations, imports and script-started coroutines share the budget; every outermost entry and host coroutine resume starts full; the nesting limit; a host fault in a callback; a coroutine ticked from its own native). New invariant (9.3): `EntryDepth` is 0 after an outermost entry. BUDGET-2 (Q-11 default), a separate commit: once the instruction budget has run out it stays exhausted (`InstructionsLeft` held at -1) until the next outermost entry, so every further instruction raises "Instruction budget exceeded" again: rescue and ensure bodies do not run and the error ends the entry. A.25 passes; the `InstructionBudgetTests` that rescued the error to observe it now check the unhandled error instead (rescue and ensure bodies skipped, the next entry starts full), as do one test each in `ConstructorFrameTests` and `RuntimeSemanticsEdgeTests`. The fuzzer's timeout entry now names BUDGET-3 (state hash part, Q-13), the only hang left. Review: a nested entry that starts while an earlier callback's error is still raised ran with that error raised, so a `begin/rescue` in the second callback caught it and the native suppressed a callback error (6.5); such an entry now runs nothing (`BeginEntry` returns false) and the error continues from the native call site (`ReentrancyTests.SecondCallbackAfterAFailedOne_DoesNotRescueTheFirstError`). |
| T1.6 | Culture-invariant, integer-only formatting | Phase 0 | done | `WarValue.FormatNumber` (public) is the one number formatter: an integer prints as invariant digits (`raw >> 32`, so `F64.MinValue` is exact); a fraction tries the rounded 1- to 9-digit candidates as before, checked with the new non-throwing `NumericLiteral.TryParseRaw` (same code path as `Parse`, which keeps its messages), and otherwise writes the exact expansion of the 32-bit fraction (at most 32 digits); no `double`, no exceptions, unsigned magnitude. Native objects never use the host `ToString()`: `F64Vec3` prints `(x, y, z)` with the number rules, a function value `<fun name>` (a lambda, whose compiled name is `<lambda>`, prints `<fun lambda>`), anything else `<TypeName>` or the text of a formatter registered with `WarScriptLanguage.RegisterNativeFormatter<T>` (exact runtime type; a null result counts as none; `F64Vec3` and functions keep their built-in format; kept across `Reload`). Registered formatters live on the instance (`NativeFormatters`, no static state), so they apply where the VM turns a value into text: `print`, text `+`/`-`, interpolation, mixed-type `==`/`!=` and text index writes, including elements of printed arrays; `WarValue.ToString()` without an instance (natives, the tree-walker, the state hash) uses `<TypeName>`. Every interpolated message in `Runtime/` with a numeric hole goes through `FormattableString.Invariant`, and `StringInterner`, the audit `Describe` and the disassembler format numbers invariantly. A.8 and the three `deep/` goldens marked DET-1 pass; A.9 (formatting table and `F64Vec3`, every culture) passes. New `Tests/FormattingTests.cs`: 40,000 pseudo-random raw values plus edges each format to a canonical form that denotes the raw value (short forms parse back, exact forms checked with `BigInteger`) and identically in every culture; native-object and function-value formats; `TryParseRaw` agrees with `Parse`; and the source scan, which lists every parameterless `ToString()` in `Runtime/` against an allow-list of non-number receivers and rejects a formatted `ToString(...)` without a culture. The scan does not look at string interpolation or `string + int`; those were reviewed by hand. Fuzz run 2026-11-13 after T1.6 (`--seed 12345 --minutes 2`, 110 corpus files): 7,886 cases, 3 signatures, none new: CRASH-6 (two) and the BUDGET-3 timeout. Review: two culture-sensitive string comparisons (`StartsWith`/`EndsWith` with a text argument, in `OperatorExtension.ToOperator` and the debugger's local listing) now pass `StringComparison.Ordinal`, and a second source scan (`Runtime_HasNoCultureSensitiveStringOperations`) rejects such calls; a registered formatter that throws `ScriptErrorException` raises a rescuable error, any other exception from it is a host fault (a formatter is host code, not a native, so 6.9 does not apply), both pinned by tests. |
| T1.7 | Opcode table, format v2, verifier | T1.3 | done | Done (first slice): explicit `OpCode` values, keeping the v1 values of the kept opcodes; `YieldUntil` (58) and the never-emitted `LessEqualJump`, `GreaterJump`, `GreaterEqualJump`, `EqualJump`, `NotEqualJump` (61 to 65) removed with their VM cases (SEM-10), values not reused. `Runtime/Bytecode/OpInfo.cs`: the `OpInfo` table of 10.1 (pops/pushes are what the instruction needs and leaves, so an instruction that only peeks, `SetLocal`, `JumpIfFalse`, `JumpIfTrue`, `ArrayAppend`'s array, counts as pop 1 push 1 rather than 0/0; `PushHandler` has a fourth operand, the `U8Flag` has-rescue byte) and `BytecodeFormat` (`Version` 2, `Layout`, `Hash`, `ComputeHash`). The disassembler decodes through the table and never throws (DX-5; A.19 passes and compares the disassembler's lines with `Chunk.InstructionStarts`). The dispatch switch has a `default:` that throws, so an unknown opcode is a host fault. The header is magic, version, hash; the loader requires both exactly (DET-4) and rejects a truncated header with `InvalidDataException`. `WarScriptLanguage.CompileWithoutRunning` (internal) exposes compiler output to tests; `Tests/BytecodeFormatTests.cs` checks the hash, the table against the enum, the header, and every function compiled from every golden against the table (opcodes, lengths, jump and handler targets on boundaries, constants, name operands). Done (second slice, SEC-1, SEC-2): `Runtime/Bytecode/BytecodeVerifier.cs` implements 10.3: decode through the table, operands (constants, `U16Name` → Text, cache slots, flags, jump and handler targets on boundaries, `Loop` backward, handler addresses ordered after the `PushHandler`), and an abstract interpretation from height = arity that checks underflow, local slots below the operands, merge consistency, rescue entry at handler height + 1 and ensure entry at handler height, and no path falling off the end; it sets `CompiledFunction.MaxStack`. Two checks beyond 10.3, both found by the mutation sweep: block scopes (`PushScope`/`PopScope`) and handlers (`PushHandler`/`PopHandler`) are tracked per frame like heights (never below 0, equal at merges, rescue and ensure entered at the `PushHandler`'s depths, both 0 at a `TailCall`), because a `PopScope` with none open popped the host's scope and `Run()` threw; and at load an arity above 255 is rejected, because the VM pads arguments with a byte counter that wraps. Every loaded function is verified while it is read (`atLoad`: `MaxStack` ≤ 1,024, arity ≤ 255); the compiler verifies all its output always, not only in Debug builds, since the VM needs `MaxStack` (a rejection there is a host fault; a function needing more than 1,024 slots is accepted and raises when called). The VM checks `base + MaxStack ≤ 1024` before pushing any frame (call, call value, tail call, method, every constructor frame, the entry frames) and before padding arguments, adding the arguments beyond the arity (a function value called with more arguments than parameters keeps them in its frame under everything it pushes; found in review, `BytecodeFormatTests.Call_WithMoreArgumentsThanTheArity_CountsTheSurplus`), raising the rescuable "Value stack overflow" for the caller; the `IndexOutOfRangeException` filter is gone. So a function whose own body needs more than the stack (an 1,100-element literal) now fails when called instead of partway through, and its own `begin/rescue` cannot catch it (`LanguageSemanticsTests.ValueStackOverflow_IsCatchable` moved the literal into a called function). Loader (10.4): a bounded reader (a non-seekable stream is copied first) turns truncation into `InvalidDataException("truncated bytecode at offset N")`, checks every count against the bytes left before allocating, requires one line per code byte and code ≤ 65,535 bytes, limits lambda-constant and class nesting to 32, decodes UTF-8 strictly; `LoadBytecode` reads and verifies before clearing anything, so a rejected file leaves the instance unchanged. The file layout did not change, so `Version` and `Hash` did not either. Tests in `Tests/BytecodeFormatTests.cs`: A.20 (each case), the further rejections, `MaxStack`, a hand-built function that loads and runs, a rejected load leaving the instance unchanged, every golden's compiled functions verified with the load-time limits and their boundaries equal to the disassembler's (A.19 compares them too), and `MutatedBytecode_IsRejectedOrRunsWithoutHostFault`: 300 seeded byte flips or truncations of each of eight compiled goldens (5,000 per file run once by hand while developing: no host fault; a 1-minute source fuzz run found only the 3 known signatures). Review of the second slice: the VM enters the script body and constructor frames without arguments, but the verifier starts every function at height = arity, so a loaded body or constructor with a nonzero arity could pop below its frame's base (an `IndexOutOfRangeException` host fault); the loader now requires arity 0 for both (`Loader_RejectsAnArityOnTheScriptBodyOrAConstructor`), and `WarVM.StackMax` is `BytecodeVerifier.StackLimit`. Done (third slice, DX-3 in part): the run-length line table (10.2). `Chunk.Lines` is a `LineTable`: `Mark(offset, line)` adds an entry only where the line changes, `GetLine` binary-searches, `Problem(codeLength)` is the verifier's check (first entry at 0, offsets strictly increasing and inside the code; no entries for empty code); the file stores a `u32` entry count (at most the code length) and `(u16 offset, i32 line)` entries. `Version` 3, new `Layout` and `Hash` (10.2 notes the numbering: T3.11's format becomes version 4). Tests: the table's encoding, a per-byte round trip through a file, the malformed tables the loader rejects. Done (fourth slice): the fuzzer's bytecode mode (9.6, notes there): `--bytecode`; the runner accepts a `.wsbc` file and reports status `rejected` for an `InvalidDataException`; `IsolatedRun.Execute` takes optional budgets (the fuzzer passes them in both modes); `BytecodeFormatTests.Runner_RunsABytecodeFile_OrReportsItRejected`. Its first run found SEM-14 (Q-15, not fixed). Review of the fourth slice: the runner's usage line names `.wsbc`. Done (fifth slice, Q-14, SEM-13 fixed, A.28 passes): `DoHandleException` no longer removes a handler with a rescue when it enters the rescue body: it puts it back with `HasRescue` cleared, an ensure-only handler with the same saved stack height and scope depth, so a `return`, `raise` or runtime error in the rescue body runs the ensure block (once) as one in the begin body does; a handler with neither rescue nor ensure block (a rescue body's, when the block has no `ensure`) is skipped. The compiler ends every rescue body with `PopHandler` (also the never-entered `Pop` of a block without `rescue`, so the code stays valid) and `break`/`next` out of a rescue body emits it (`TryRegion.Rescue`); the verifier enters a rescue body with one more handler open. Compiled code changed, the opcode table did not: `Version` 4, new `Layout` and `Hash`; T3.11's format becomes version 5. Tests: A.28 (no longer known-failing) also checks a rescue body without ensure, a normal end and a `break` leaving no handler behind, and nested ensure blocks; `BytecodeFormatTests.Verifier_EntersARescueBodyWithItsEnsureOnlyHandlerOpen` rejects a version 3 rescue body. A 90-second fuzz run in each mode (seed 20261113) found only known signatures. Open from T1.7's runs: SEM-14 (Q-15). Review of the fifth slice: every rescue body held a handler slot, so a recursion from a rescue body (a retry: `rescue` / `return retry [n - 1]`) deeper than 32 raised "Exception handler stack overflow", where v1 ran it. Only a block with an ensure block needs the ensure-only handler: the VM puts the handler back only then, the compiler emits the rescue body's `PopHandler` (at its end and for `break`/`next`) only then, and the verifier enters the rescue body with one more handler only when the ensure address differs from the end address; the skip of a handler with neither rescue nor ensure block in `DoHandleException` is gone. Still `Version` 4 (never released), new `Layout` and `Hash`. A.28 adds a 60-deep retry and `next`/`break` out of a rescue body without ensure inside an outer `begin`; `BytecodeFormatTests.Verifier_EntersARescueBodyWithoutAnEnsureBlockWithNoHandlerOfItsBlock`. A recursion from the rescue body of a block *with* an ensure block is limited like one from a begin body. |
| T1.8 | Compiler limit checks | Phase 0 | done | Done (first slice, DX-2 fixed): `CompileError` (`Runtime/Exception/CompileError.cs`) derives from `SyntaxException`, so it leaves `Run()` as compile errors already do; it carries `Reason`, `ScriptName` and `Line`, and its message is the reason, `at line N` and `in '<script>'`. `Chunk` raises it with line 0 and the script name (`Chunk.Source`); `Compiler.CompileStatement` fills in the line of the innermost statement that has one (expressions are emitted with line 0 until T1.10). `EmitU16` rejects a value outside 0 to 65,535; `PatchJump`, `EmitLoop` and the new `Chunk.PatchAddress` (handler addresses and `DropPending` targets) raise "function too large (bytecode exceeds 65,535 bytes)"; `VerifyOutput` raises the same for straight-line code past 65,535 bytes (instead of the verifier's host fault) and "too many local variables" above 65,535 slots. `Chunk.AddConstant` raises when a 65,536th constant would be added (`Chunk.MaxConstants` = 65,535, as this section says; an index of 65,535 would still encode). Call, tail call, call value, method call, `new` and nested `new` take their count through `EmitArgCount`: "more than 255 arguments in a call"; a function or lambda with more than 255 parameters is "more than 255 parameters" (the loader rejects such an arity too). `Chunk.EmitPops` splits a `PopN` above 255. An array literal of more than 65,535 elements is "array literal too large; build it with <<", checked before its elements are compiled. Unreachable from source, so tested on `Chunk` directly: the constant pool (each constant use costs at least 3 code bytes, so a function runs out of code first), a `PopN` above 255 (only loop variables and rescue variables are stack locals, a few per scope) and more than 65,535 locals; a 65,535-element literal is "function too large". Tests: `Tests/CompilerLimitTests.cs`: for straight-line code, an `if` body (forward jump), a loop body (backward jump) and a `begin` body (handler addresses), the largest generated source that fits compiles and one more statement raises; 255 arguments run and 256 raise for each call form, with the line; 255 and 256 parameters for functions and lambdas; the array literal; the constant pool, `EmitU16` and `EmitPops` on a `Chunk`. Compiled code within the limits is unchanged, so the format version is not. Done (second slice, CRASH-6 fixed, Q-11): `OperatorExtension` raises `SyntaxException` instead of `System.Exception` for a string or operator it cannot map; the expression reader raises "Unbalanced '(' at line N: missing ')'" when a `(` is still open at the end of an expression (A.23a, which used to reach `ToBinaryExpression` with `LeftParen`); the compiler raises the `CompileError` "'as' must be followed by a class name" (or `'is'`) when the right side of `as`/`is` is not a plain name (A.23b, which used to throw `InvalidCastException`). These were the only unchecked casts in the compiler. A.23a/b pass; goldens `language/unbalanced_paren.ws` and `language/instance_of_needs_class_name.ws` cover the other shapes; the fuzzer's CRASH-6 entry is gone from `KnownFindings`. Review: the unbalanced-`(` message names the line of the expression's last token, as the unbalanced-`)` one does. |
| T1.9 | Lexer cache and static state | Phase 0 | done | DX-1 and DX-6 fixed. `LexicalParser.Parse` returns `IReadOnlyList<Token>` (a `ReadOnlyCollection` over the lexed list, so a caller cannot change what the cache holds; `StatementParser.Parse` and `TokensStack` take `IReadOnlyList<Token>`). The cache is `TokenCache` (`Runtime/Parser/TokenCache.cs`): least recently used entry evicted beyond `LexicalParser.CacheCapacity` (64, a const: a settable capacity would itself be static mutable state), every access under the cache's lock. Lexing happens outside the lock; when two threads lex the same source, the list cached first is returned to both. `ClearCache()` kept. `Tests/StaticStateTests.cs`: the reflection test over every non-compiler-generated type of the runtime assembly (FixPointCS's namespaces excluded, since the headless build compiles it in) fails on a static field that is neither `readonly` nor `const` and on a `readonly` static field of an array or `ICollection` type, unless it is on the allow-list, each entry with its reason; a second test fails on an allow-list entry that names no field. Besides the three tables DX-6 named and the lexer cache, the test found six private constant arrays (`StatementParser.StatementLeadTypes`, `IfBranchKeywords`, `CommaValue`, `WarValue.Pow10`, `BytecodeSerializer.Magic`, `OpInfo.ByOpcode`), allow-listed as initialized once and only read, and `ExpressionReader._pool`, the `[ThreadStatic]` pool of readers, allow-listed because each thread has its own and it holds only cleared readers. Tests for LRU eviction, the bound, read-only results, and 8 threads lexing 128 sources through the 64-entry cache while one of them clears it. Review: `WarScriptLibraryRegistry.Libraries` was a public array, so a host could replace an entry and change the standard library of every instance; it is now an `IReadOnlyList<LibraryInfo>` (`Array.AsReadOnly`; `LibraryInfo` is a struct, so an entry read from it is a copy) and off the allow-list (`LibraryRegistry_CannotBeChangedByTheHost`). The reflection test no longer flags a `ReadOnlyCollection`/`ReadOnlyDictionary` field (it implements the non-generic `ICollection`, so the `ReadOnly` exception never applied). |
| T1.10 | Complete line information | T1.7 | done | DX-3 fixed. Expressions have no line of their own in the AST, so the compiler keeps `_line`, the line of the statement being compiled: `CompileStatement` sets it from the statement's `RowNumber` and restores it afterwards, a desugared statement without a row keeps the enclosing line, and a function, constructor or lambda body starts at its definition's line. Every emit in `CompileExpression`, the call, method-call, `new`, nested-`new` and property paths, `as`/`is`, sub-expression assignments and the `and`/`or` jumps uses it instead of 0; the T1.8 limit errors raised there now name that line directly. Only the implicit `Null`, `Return` at the end of every function keeps line 0 (it belongs to no statement; the debugger does not stop on line 0). A multi-line expression is reported at its statement's first line; finer positions come with columns (T3.1). Runtime errors have no line in their v1 message (the assertion error excepted), so the plan's "golden test per family that checks the error's line" is not possible before T2.5: the golden runner does not check lines. Tests instead: `Tests/LineInfoTests.cs` compiles one statement per instruction family (call, call value, method call, `new`, nested `new`, property, `this` property, array literal, indexing, arithmetic, `and`, `is`, `as`) and checks the line of each instruction, the operands' lines, a multi-line call, and the debugger's call stack showing the caller's line for a call and a `new` (it showed line 0 before); `BytecodeFormatTests.CompiledGolden_EveryInstructionHasALine` checks every function compiled from every golden. Lines are data in the existing line table, so the format version and hash are unchanged. T2.5 should add the error line to the goldens' `expect-error` checks. |
| T2.1 | Remove the tree-walker | Phase 1 | done | API-3 fixed. Statements and expressions are plain data: `Statement.Execute()`, `IExpression.Evaluate()` (`IExpression` is now an empty marker interface), every node's `Execute`/`Evaluate`, `IAssignExpression` and its `Assign` methods, the loops' `Init`/`HasNext`/`PreIncrement`/`PostIncrement` and their per-execution fields, and the lambda's compile-on-evaluate cache are gone, as are `Coroutine/Coroutine.cs`, `CallTreeWalk`, `Compiler.CompileLambda(script, lambda)` (its only caller was `LambdaExpression.Evaluate`), `BreakContext`/`BreakScope`/`NextContext`/`NextScope`/`ReturnContext`/`ReturnScope` with their fields, `ExceptionContext.AddTracedStatement` and the statement list in `Exception/Exception.cs` (an unhandled error still logs its value and a line break, as before: the VM never added statements to it), and `ValueReference` no longer implements `IExpression` (its `InstanceOf(IExpression)` went with it). `HaltFlags` had no reader left once the AST stopped executing, so it is removed whole rather than shrunk; the yield state (`IsYielded`, `SetYielded`, `ClearYield`) stays on `WarScriptLanguage` because hosts and tests read `IsYielded` after a plain `Call` (moving it is left to T2.3/T2.4 with the result types). The two entries that used to fall back to the tree-walker now refuse: `Call` on a function without bytecode (in practice a native function: every script function has bytecode once the script is compiled, and the old fallback dereferenced the native's null statement) logs `[WarScript] Cannot call '<name>' in '<script>': it has no bytecode` and returns; `StartCoroutine` on one returns -1 with the error "Coroutine function '<name>' with N arguments has no bytecode" raised, like its undefined-function case (`CoroutineHostApiTests.Call_OnAFunctionWithoutBytecode_LogsAndDoesNothing`, `StartCoroutine_OnAFunctionWithoutBytecode_ReturnsMinusOneAndRaises`). T2.3 turns both into `Error` results. The binding generator built a `[WsEnum]` instance with `ClassExpression.Evaluate()`; it now creates the `ClassData` directly (the enum class has no fields, base classes or constructor body, so that is all `Evaluate` did; checked by compiling the generator's output against the runtime in a throwaway test). Tests: `ParserTests` reads `ConstantExpression.Value`; the two `ImportBytecodeTests.TreeWalkImport_*` tests are deleted with the statement's `Execute` (the VM path has the same cases: `ImportedBytecode_ExecutesTopLevelCode`, `UnusableBytecode_WarnsAndCompilesSource`). The `.meta` files of deleted files are deleted (R3). The optional collapse of the operator classes into `BinaryExpression`/`UnaryExpression` is not done. Review: a generated `[WsEnum]`'s `name[]` method was an AST-only `FunctionDefinition` that nothing compiled (the compiler walks the user's definitions, not the host's), so `E :: name [v]` raised "Method 'name' is not compiled" (before this change too); the generator now registers it as a native method that compares a numeric argument with each member and returns `"unknown"` otherwise (a text argument no longer matches its member, unlike a script enum's v1 `==`, SEM-6). Checked with `.claude-task/tools/check-binding-generator.sh`, which compiles the generator with Unity stubs, generates bindings for a sample module and runs a script against them. Also found, not fixed: `ConstantNames` is `internal`, so generated `[WsConst]`/`[WsEnum]` code (which calls `ConstantNames.Add`) does not compile in an assembly other than the friends in `AssemblyInfo.cs`; T2.6/T3.8 should give the generator a public way to declare host constants. |
| T2.2 | Explicit compile step | T2.1 | done | `WarScriptLanguage.Compile()` returns a `CompileResult` (`Runtime/CompileResult.cs`: `Success`, `Diagnostics`; a `CompileDiagnostic` has `Severity` (`DiagnosticSeverity.Error`/`Warning`), `Message`, `ScriptName`, `Line`, `Column` and prints as `script:line:col: error: message`). It lexes, parses and compiles once and caches the result, success or failure, until `Reload` replaces the source; `LoadBytecode` sets it to success. The constructor no longer lexes (it keeps the source), so a lexer error is a diagnostic too instead of an exception from `new`. A `SyntaxException` (including a T1.8 `CompileError`) becomes one error diagnostic: a `CompileError` gives its `Reason`, `ScriptName` and `Line`; any other parser or lexer error gives its message and line 0 (several v1 messages name the line in the text; positions for every diagnostic come with T3.1). After a failed compile the user definition scope is cleared, so `GetFunction` finds none of the half-parsed functions. Any other exception from the compiler (a compiler bug the verifier rejects) still propagates and nothing is cached. `Run`, `Call` and `StartCoroutine` compile when needed; `StartCoroutine` before `Run` now starts the coroutine (before, the function was not defined yet). Deviation until T2.3/T2.4: there is no `ScriptResult` and no halting yet, so a compile error is still thrown from the entry points as the cached `SyntaxException` (`Run` throws it before `BeginEntry`, so no context is pushed), and `Reload` still throws it. Errors in imported files are found when the import runs, until Phase 3 resolves imports in `Compile`. Tests: `Tests/CompileTests.cs`. Review: the `const`/`enum` names the parser had added stayed in `ConstantNames` after a failed compile, and after an uncached compiler exception the half-parsed definitions and names stayed too, so the next `Compile()` reported the script's own `const` as reassigned; every failed compile now clears the definitions and puts `ConstantNames` back to what it was before (host names kept; `FailedCompile_LeavesNoConstantNames_ButKeepsTheHostOnes`). |
| T2.3 | Result types | T2.2 | done | API-1 fixed; API-2 fixed for the host (the result carries the error; the instance is not halted until T2.4). `Runtime/ScriptResult.cs`: `ScriptStatus` (Ok, Error, Halted, HostFault), `ScriptFrame`, `ScriptError` (`Kind`, `Message`, `Value`, `ScriptName`, `InstanceLabel` (null until T2.8), `Line`, `Column`, `CallStack` (empty until T2.5), `HostException`; `ToString()` is `script:line:col: Kind: message` plus one `  at function (script:line:col)` line per frame) and `ScriptResult` (`Status`, `Value`, `Error`, `IsOk`). `Run()` returns Ok with a null value (the VM keeps no top-level return value); `Call` returns the function's return value; `StartCoroutine(name, args, loop, out id)` returns the first run's result; `TickCoroutines(dt)` returns Ok with the number of active coroutines, or the first error a resume ended with (the other coroutines still run until PROP-3 in T2.4). A compile error is an `Error` result of kind CompileError (no longer thrown, so T2.2's deviation is gone), logged as `[WarScript] ` + the error, before `BeginEntry`; a `SyntaxException` from compiling an import passes through nested entries and becomes a CompileError result at the outermost one (`ExceptionBoundaryTests.SyntaxErrorInImport_*`). Host faults are `HostFault` results with `HostException`. An unhandled error is still logged and cleared at the outermost entry; a nested entry's result carries it and leaves it raised for the calling script (6.5, PROP-1). `Call` on a function without bytecode and `StartCoroutine` on a missing (NameError) or native (TypeError) function return an `Error` result; at the outermost level the error is logged like an unhandled one instead of being left raised (T2.1 left `StartCoroutine`'s raised with nothing to clear it), nested (`start_coroutine` from a script) it stays raised for the script. Kinds: `ScriptErrorKind` gained CompileError and HostFault and moved to namespace `WarScript` as 6.1 has it (`ScriptErrorException` stays in `WarScript.Exception`; a host file that only imports `WarScript.Exception` needs `using WarScript;`). Until T2.5 assigns a kind at every raise, the kind is known for errors thrown as `ScriptErrorException` (incl. LimitExceeded from the value-depth and length guards), native failures (NativeError) and the host nesting limit; every other runtime error reports Raised, and runtime errors have line 0. Deviations from 6.1: `Call` still takes a `FunctionDefinition` (handles are T2.6) and no `ReadOnlySpan` overloads (T2.7); the `int StartCoroutine(name, args, loop = false)` overload stays as a convenience (the `start_coroutine` native uses it), so T2.7 should decide whether to drop it. `ICoroutine.Resume()` returns the `ScriptResult`. The golden runner reads compile errors from `Run()`'s result; `TestHelper.Run` rethrows a CompileError result's `SyntaxException`, so the v1-style tests that expect one are unchanged. Tests: `Tests/ScriptResultTests.cs`; updated `CompileTests`, `CoroutineHostApiTests`, `ExceptionBoundaryTests`, `CompilerLimitTests`. Found, not fixed: `TickCoroutines` indexes `_coroutines` by position while a resume can stop other coroutines (`stop_coroutine`, `StopAllCoroutines` from a native), so the list may shrink under it (wrong coroutine removed, or an `ArgumentOutOfRangeException`); T2.4's PROP-3 rewrite of the tick loop should handle it. |
| T2.4 | Fault and halt model | T2.3 | todo | |
| T2.5 | Structured errors | T2.3 | todo | |
| T2.6 | Function handles that survive Reload | T2.3 | todo | |
| T2.7 | Allocation-free call overloads | T2.3 | todo | |
| T2.8 | Instance label | T2.5 | todo | |
| T2.9 | Host API documentation | T2.1 to T2.8 | todo | |
| T3.1 | Columns (on the v1 frontend) | Phase 2 | todo | |
| T3.2 | Migration tool (v1 frontend; tag `warscript-v1-final`) | T3.1 | todo | |
| T3.3 | Frontend v2 | T3.2 | todo | |
| T3.4 | Name resolution and compiler | T3.3 | todo | |
| T3.5 | VM frame layout and scope removal | T3.4 | todo | |
| T3.6 | Operator, equality, indexing and formatting semantics | T3.4 | todo | |
| T3.7 | Static imports and module initialization | T3.4 | todo | |
| T3.8 | Compile-time diagnostics (widened: PROP-27 to PROP-31) | T3.4, T3.7 | todo | |
| T3.9 | Run the migration (game scripts compiled clean against the native manifest) | T3.3 to T3.8 | todo | |
| T3.10 | Language spec and documentation | T3.9 | todo | |
| T3.11 | Bytecode format v3 | T3.5, T3.7 | todo | |
