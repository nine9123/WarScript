# WarScript: Incremental Improvements

| | |
|---|---|
| Status | Revised 2026-11-26, against `main` after T3.14 (migration tool and audit mode removed) |
| Location | `Documentation~/IMPROVEMENTS.md` (the `~` folder needs no `.meta`) |
| Follows | `Documentation~/WARSCRIPT_V2_PLAN.md`: Phases 0 to 3 are done. This list replaces its section 11 ("Deferred work") as the working backlog. |

## How this document works

**Every task is one pull request, and each one stands alone.** No task needs another task to be
merged first. Pick any unchecked task, hand it off, merge it, and tick the box. The order below is
a recommendation (cheap and protective first, risky later), not a dependency chain.

Each task lists:

- **Size**: S (under a day, small diff), M (a focused PR), L (a large PR that is still one change).
- **Touches**: the main files. Two in-flight tasks that touch the same file do not depend on each
  other, but whichever merges second has to rebase. "Collides with" names the usual suspects.
- **Format bump**: whether it changes the bytecode format (`BytecodeFormat.Version`,
  `Runtime/Bytecode/OpInfo.cs:264`, currently 14). Every format bump is standalone. If two are in
  flight at once, the one that merges second rebases and takes the next number.
- **Owner decision**: whether the owner has to answer a design question *before* implementation
  starts. The question is written in the task. Put the answer in the task, then hand it off.
- **Done when**: the acceptance criteria. A PR is not done until all of them hold.

Line numbers are from the `main` snapshot taken after T3.14 and may drift. Each task names the
methods as well, so search for those when the numbers are off.

**Task IDs never change.** A finished or dropped task keeps its ID, gets ticked or struck
through, and loses its detail section, so IDs already handed off still point to the right thing.

### Done outside this list

| What | Where | Affects |
|---|---|---|
| Migration tool, legacy lexer, v1 frontend and audit mode removed | v2 plan T3.14 (last commit with the tools: `510e6df`) | B5 and B6 are done. B3 has no audit hooks left to move. |
| `package.json` bumped to `0.6.0` | `main` | A4 is reduced to the CHANGELOG and the version policy. |
| Warman native manifest committed (`Documentation~/migration/warman.WarScriptNatives.wsmanifest`, 140 natives) | `main` | Usable as a test fixture in F2 and F3, and for checking C task names against the game's natives. |

Still open from T3.14: the tag `warscript-migration-tools-final` on `510e6df` exists only in the
implementer's clone. The owner creates it.

### Standing rules for every PR

These carry over from the v2 plan (section 0.1, R1 to R8):

1. **Determinism (R1).** No `float`/`double`, `System.Random`, wall clock or `UnityEngine` in
   `Runtime/`. String operations are ordinal or invariant-culture.
2. **Unity compatibility (R2).** The runtime compiles as `netstandard2.1` with C# 9.0. Tests stay
   within the NUnit 3.5 API surface.
3. **`.meta` files (R3).** Every new file or folder outside a `~` folder gets a `.meta` with a fresh
   GUID (templates are in plan Appendix C). Deleting a file deletes its `.meta`.
4. **Tests ship with the change (R5).** Prefer golden `.ws` files under `Tests/Golden~/`.
5. **Docs ship with the change (R7).** Update `CLAUDE.md`, `README.md` and
   `Documentation~/LANGUAGE.md` in the same PR whenever behavior changes.
6. **Ask, don't invent (R8).** If the task leaves a semantic question open, stop and ask.
7. **One task per PR.** Put the task ID in the PR title and the commits, for example `C1: text basics`.
   Don't fold in drive-by work from another task.
8. **Green headless suite.** `dotnet test Headless~/Tests` passes, and any `Benchmark` numbers the
   task cites are measured before and after.

### Hand-off prompt

Commit this file to `Documentation~/IMPROVEMENTS.md` first. It is not on `main` yet, so an agent
working from `main` can't read it.

> Read `CLAUDE.md`, then implement task **&lt;ID&gt;** from `Documentation~/IMPROVEMENTS.md`, and only
> that task. Follow its "Done when" list and the standing rules. If the task says an owner
> decision is needed and no answer is recorded, stop and ask. Open one PR.

---

## Checklist

Recommended order, top to bottom. Tracks can be interleaved.

**A. Housekeeping**
- [ ] A1 — CI: headless tests on Linux, Windows and macOS (S)
- [ ] A2 — Fix stale v1 syntax in the docs (S)
- [ ] A3 — Test that compiles the code samples in the docs (S)
- [ ] A4 — CHANGELOG and version policy (S)
- [ ] A5 — Slim down `CLAUDE.md` (M)

**B. Structure (moves only, no behavior change)**
- [ ] B1 — Split `WarScriptLanguage.cs` into partial files (S)
- [ ] B2 — Split `Compiler.cs` into partial files (S)
- [ ] B3 — Split `WarVM.cs` helpers into partial files (S)
- [ ] B4 — Hot reload without `LexicalParser.ClearCache()` (S)
- [x] ~~B5 — Remove the v1 migration tool and legacy lexer~~ (done in v2 plan T3.14)
- [x] ~~B6 — Remove audit mode~~ (done in v2 plan T3.14)
- [ ] B7 — Decide the future of `ModTrace` and `BASELINE.md` (S, owner decision)

**C. Standard library**
- [ ] C1 — Text basics: length, substring, search (S)
- [ ] C2 — Text transforms: case, trim, replace, split, join (M)
- [ ] C3 — Parse a number from text (S)
- [ ] C4 — Array extras: slice, reverse, sort (M)
- [ ] C5 — Array functions that take a function value (M, owner decision)
- [ ] C6 — Map type as a native library (L, owner decision)

**D. Performance** (from v2 plan section 11.1, each with before and after benchmarks)
- [ ] D1 — `Chunk.Code` as `byte[]` (M)
- [ ] D2 — Smaller per-instance footprint (S)
- [ ] D3 — Opcode-pair histogram (S)
- [ ] D4 — Allocation-free native calls (L)
- [ ] D5 — Call-site inline caches (L)
- [ ] D6 — `ConcatN` for string interpolation (M, format bump)
- [ ] D7 — `StoreLocal` superinstruction (M, format bump)
- [ ] D8 — `Dup2`: compound assignment evaluates its target once (M, format bump)

**E. Language**
- [ ] E1 — `@method()` sugar (S)
- [ ] E2 — Structured errors in `rescue` (M, owner decision)
- [ ] E3 — Numeric overflow detection (M, owner decision)
- [ ] E4 — Lambdas capture locals by value (L, owner decision, likely format bump)
- [ ] E5 — Call any expression: `arr[0](x)`, `f(x)(y)` (M, format bump only if needed)

**F. Tooling**
- [ ] F1 — VS Code syntax highlighting (S)
- [ ] F2 — `wsc` command-line tool (M)
- [ ] F3 — Diagnostics-only language server (L)

**G. Testing**
- [ ] G1 — Coverage-guided fuzzing with SharpFuzz (M)
- [ ] G2 — Mutation-testing report with Stryker.NET (S)

---

## A. Housekeeping

### A1 — CI: headless tests on Linux, Windows and macOS
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** There is no CI. For a lockstep runtime, the most valuable check is the same test suite,
  with the same state hashes, on x64 and ARM across operating systems.
- **Scope.** `.github/workflows/test.yml`. Unity ignores dot-folders, so it needs no `.meta`. Use a
  matrix of `ubuntu-latest`, `windows-latest` and `macos-latest` (arm64). Steps: set up .NET 8, run
  `Headless~/fetch-fixpointcs.sh` (via Git Bash on Windows) with a cache keyed on the pinned commit,
  then `dotnet test Headless~/Tests --filter TestCategory!=Benchmark`. Upload `Headless~/out/` as an
  artifact. If the golden state hashes are not already pinned in the repository, add a final job
  that downloads every platform's hash files and runs `Headless~/compare-hashes.sh` across them.
- **Out of scope.** IL2CPP, Unity Test Runner, fuzzing.
- **Done when.** The workflow runs on push and PR, all three platforms pass, and a deliberate hash
  difference (tested locally) makes the cross-platform job fail. The README gets a status badge.

### A2 — Fix stale v1 syntax in the docs
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** DEC-22 (T3.12) changed calls to `f(x)` and arrays to `[1, 2]`, but some text still uses
  the old forms. Examples: `README.md:92` (`tick[dt]`), and F-LANG-4 in the v2 plan
  (`WARSCRIPT_V2_PLAN.md:1829`, `this :: method [args]`). Both are still present after T3.14.
- **Scope.** Grep `README.md`, `CLAUDE.md`, `Documentation~/*.md` and the XML doc comments in
  `Runtime/` for `name [args]` calls, `{1, 2}` arrays and `::` members. Fix every hit that describes
  current behavior. Leave the history alone: `Documentation~/migration/`, the v2 plan's findings and
  repro sections, and the error messages that hint at the new syntax when they see the old one.
- **Done when.** The grep finds only intentional legacy references. No code changes.

### A3 — Test that compiles the code samples in the docs
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** This catches the kind of drift A2 fixes, permanently. It works whether or not A2 has
  landed: any broken sample it finds gets fixed in the same PR.
- **Scope.** A new NUnit test (with `.meta`) that extracts the WarScript fenced blocks (the
  `warscript`/`ws` language tags; tag any untagged ones) from `README.md` and
  `Documentation~/LANGUAGE.md`, compiles each with the stdlib registered, and asserts no errors.
  A sample that is deliberately wrong is marked with an HTML comment just before the fence
  (`<!-- expect-error -->`) and must fail to compile instead. Fragments that need host bindings
  can be marked `<!-- no-compile -->`.
- **Done when.** The test runs in the headless suite and every unmarked sample compiles.

### A4 — CHANGELOG and version policy
- **Size** S · **Format bump** no · **Owner decision** what version marks "v2 complete": `1.0.0`
  now, or stay on `0.x` until the C and E tracks settle?
- **Why.** `main` is on `0.6.0`, but nothing records what changed between versions. A UPM
  package shows `CHANGELOG.md` in the Package Manager.
- **Scope.** Add `CHANGELOG.md` at the root (with `.meta`) in Keep a Changelog form. `0.6.0`
  summarizes v2 by area (language, host API, bytecode format, safety, removed tools) and links the
  v2 plan for detail. Keep a `## [Unreleased]` section. Add a line to `CLAUDE.md`: every PR adds one
  line under Unreleased, and a PR that bumps `BytecodeFormat.Version` says so in that line. Set
  `package.json` to whatever version the owner chose.
- **Done when.** The CHANGELOG and the `CLAUDE.md` rule are in place, and the owner tags the
  release after merge.

### A5 — Slim down `CLAUDE.md`
- **Size** M · **Format bump** no · **Owner decision** no
- **Why.** `CLAUDE.md` is still about 89 KB after T3.14. The "Key Invariants & Gotchas" section alone is about 56 KB, and
  "Testing Conventions" about 11 KB, mostly per-task implementation history (T1.x, T3.x). Every agent
  session loads all of it.
- **Scope.** Keep in `CLAUDE.md`: the rules, the invariants as one or two lines each, project
  structure, the pipeline, how to add natives, and how to run tests. Move the history and long
  explanations to `Documentation~/INTERNALS.md`, grouped by subsystem (lexer, compiler, VM,
  serializer, host API), and link to it from each `CLAUDE.md` section. **Move, don't delete.**
- **Done when.** `CLAUDE.md` is under about 25 KB, every removed paragraph exists in
  `INTERNALS.md`, and no invariant is lost. The reviewer checks this by diffing the two.

---

## B. Structure

The three split tasks are mechanical moves, which makes them cheap to review. Doing them before
the D tasks means the performance PRs land on smaller files, but D does not depend on them.

### B1 — Split `WarScriptLanguage.cs` into partial files
- **Size** S · **Format bump** no · **Owner decision** no · **Collides with** B4, C6
- **Scope.** Make `WarScriptLanguage` a `partial class`. Move:
  - The compile-time module-set checks (`CheckInstantiation` to `ConstructorArityMessage`, about
    lines 1049 to 1660) → `WarScriptLanguage.Checks.cs`
  - Import resolution (`ResolveImports` to `FindImportedClass`, about 833 to 1020) →
    `WarScriptLanguage.Imports.cs`
  - `SaveBytecode`/`LoadBytecode`/`LoadImportedBytecode` → `WarScriptLanguage.Bytecode.cs`
  - Coroutine API (`SetYielded`, about line 2205, to the end) → `WarScriptLanguage.Coroutines.cs`
  - Entry, fault and halt handling (`BeginEntry`/`EndEntry`/`FinishEntry`/`Halt`/`ClearFault`,
    `Run`, `Call`, `HostFault*`) → `WarScriptLanguage.Entries.cs`
- **Done when.** Only whole members move: `git diff --color-moved=zebra` shows nothing but moved
  blocks, `using` lines and `partial` keywords. New files have `.meta`. Tests pass.
  `CLAUDE.md` "Project Structure" lists the new files.

### B2 — Split `Compiler.cs` into partial files
- **Size** S · **Format bump** no · **Owner decision** no · **Collides with** D6, D7, D8, E4, E5
- **Scope.** Make `Compiler` a `partial class`. Move:
  - Definitions, classes and constructors (`CompileDefinitions` to `CompileLambdaBody`, about
    lines 225 to 550) → `Compiler.Definitions.cs`
  - Statements (`CompileStatement` to `CompileYield`, about 709 to 1760) → `Compiler.Statements.cs`
  - Expressions (`CompileExpression`, `CompileExpressionAt`, the call, instantiation and property
    compilers) → `Compiler.Expressions.cs`
  - Static typing (`StaticType`, `TypeOf`, `CheckOperands` to `ConstantType`, about 2046 to 2280) →
    `Compiler.Types.cs`
- **Done when.** Same as B1: moves only, `.meta` files present, tests pass, docs list the files.

### B3 — Split `WarVM.cs` helpers into partial files
- **Size** S · **Format bump** no · **Owner decision** no · **Collides with** all D tasks, E5
- **Scope.** Make `WarVM` a `partial class`. `ExecuteLoop` (about lines 427 to 1847) **stays a
  single method in `WarVM.cs`.** Breaking up the dispatch switch is a performance question, not a
  refactor. Move:
  - Exception handling and pending returns (`DoHandleException`, `PushPending` to `ClearPending`) →
    `WarVM.Exceptions.cs`
  - Errors and limit checks (`RuntimeError`, `RaiseNativeFailure`, `TrackAlloc`, `Check*Length`,
    `CheckIndex`, the `Estimate*` helpers, the `*Message` builders) → `WarVM.Checks.cs`
  - Class instantiation and methods (`InstantiateClass`, `AllocateInstance`, `EnterConstructors`,
    `FindClassWithMethod`, `CheckDeclared`) → `WarVM.Classes.cs`
- **Done when.** Same as B1. A `Benchmark` run shows no regression beyond noise.

### B4 — Hot reload without `LexicalParser.ClearCache()`
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** The README tells hosts to call `LexicalParser.ClearCache()` by hand for hot reload
  (`README.md:1429`, and the gotcha at `README.md:1587`; also `CLAUDE.md:342`).
  That is a trap for anyone embedding the runtime.
- **Scope.** First find out why a cache keyed by source text needs clearing at all: line or column
  data, the script name, or the token objects themselves. Then fix the cause so that `Reload()` is
  correct without the call. Keep `ClearCache()` as a memory-release API only.
- **Done when.** There is a regression test that reloads changed and unchanged sources without
  calling `ClearCache()` and gets correct behavior and error positions. The gotcha is removed from
  `README.md` and `CLAUDE.md`.

### B5, B6 — Done
Removed in v2 plan T3.14. Nothing left to do here.

### B7 — Decide the future of `ModTrace` and `BASELINE.md`
- **Size** S · **Format bump** no
- **Owner decision.** `Headless~/ModTrace` and `Documentation~/migration/BASELINE.md` were built
  for T0.9 (behavior baseline), which DEC-20 dropped. T3.9 still used ModTrace once, to show that
  the warman mod's native-call order matches v1 (`migration/warman.md`). Pick one:
  1. **Keep it as a regression tool.** It is the only check against the real mod, which is useful
     before merging any E task. Document how to run it in `CLAUDE.md` under Testing.
  2. **Remove it.** Delete `Headless~/ModTrace/` and `BASELINE.md`, and point `warman.md` at the
     commit that last had them.

  The suggestion is option 1, because the E tasks change semantics.
- **Scope.** Whichever option is chosen, plus any remaining references (`CLAUDE.md`, the v2 plan
  file map).
- **Done when.** No document describes ModTrace as part of the dropped T0.9 workflow without saying
  what it is for now.

---

## C. Standard library

Each task adds natives to `Runtime/Native/Libraries/` (adding a file means adding a `.meta`), with
goldens in `Tests/Golden~/stdlib/` (a new `~` subfolder, so no `.meta`). Each also documents the
functions in the README's "Standard Library" section and in `LANGUAGE.md`, and updates the native
manifest snapshot if `NativeManifestTests` covers the stdlib. Naming follows PROP-7
(`Text_remove`), and a new name must not clash with a native in
`Documentation~/migration/warman.WarScriptNatives.wsmanifest`. Text positions are 0-based UTF-16 code units, consistent with `s[i]` indexing.
Bad argument types raise `TypeError` and bad positions raise `IndexError`, as indexing does.

### C1 — Text basics: length, substring, search
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** This answers v2 Q-17. Scripts currently probe `s[n]` inside `begin`/`rescue` to find a
  text's length.
- **Scope.** `Text_length(s)`, `Text_sub(s, start, count)`, `Text_find(s, sub)` (index or `-1`),
  `Text_contains(s, sub)`, `Text_starts_with(s, prefix)`, `Text_ends_with(s, suffix)`. All ordinal.
- **Done when.** There are goldens for normal, empty and out-of-range cases. Q-17 is marked
  answered in the v2 plan, and `Tests/Golden~/deep/test_deep_string_algos.ws` drops its probe loop
  in favor of `Text_length`.

### C2 — Text transforms: case, trim, replace, split, join
- **Size** M · **Format bump** no · **Owner decision** no
- **Scope.** `Text_upper`/`Text_lower` (`ToUpperInvariant`/`ToLowerInvariant`), `Text_trim`,
  `Text_replace(s, old, new)`, `Text_split(s, sep)` → array, `Text_join(arr, sep)`.
  `Text_replace`/`Text_join` can grow text, so they check `MaxTextLength` and the memory budget
  *before* allocating, as text `+` does (T1.4). `Text_split` checks `MaxArrayLength`. `Text_join`
  formats elements with the canonical formatter (`WarValue.FormatNumber` for numbers).
- **Done when.** There are goldens for each function, including the limit errors
  (`LimitExceeded`), and a culture-matrix run (the existing T0.4 harness) shows identical output.

### C3 — Parse a number from text
- **Size** S · **Format bump** no · **Owner decision** no
- **Scope.** `Text_to_number(s)` returns the number, or `null` when `s` is not a numeric literal.
  It reuses `NumericLiteral.TryParseRaw`, so parsing stays integer-only and deterministic and
  round-trips with the formatter: `Text_to_number("" + n) == n`.
- **Done when.** There are goldens for integers, fractions, negatives, separators (`1_000`, or
  rejected, matching the lexer), garbage and overflow, plus a property test of the round trip over
  random raw values.

### C4 — Array extras: slice, reverse, sort
- **Size** M · **Format bump** no · **Owner decision** no
- **Scope.** `Array_slice(arr, start, count)` → new array, `Array_reverse(arr)` in place,
  `Array_sort(arr)` in place. Sorting is stable, with all-number arrays by value and all-text arrays
  by ordinal. Mixed or other element types raise `TypeError`. Use a stable algorithm written out
  explicitly: `List.Sort` is not stable, and its exact algorithm must not become a determinism
  dependency. Account allocations against the memory budget.
- **Done when.** There are goldens, including stability with equal keys and the mixed-type error.

### C5 — Array functions that take a function value
- **Size** M · **Format bump** no
- **Owner decision.** How does a native call a script function value: through the existing
  nested-entry path (DEC-16, plan 6.5)? And does an error inside the callback propagate as a normal
  rescuable script error? Suggested default: yes to both, with no isolation (F-LANG-5 stays
  deferred).
- **Scope.** `Array_map(arr, f)`, `Array_filter(arr, f)`, `Array_sort_by(arr, f)` (stable, keys as
  in C4), `Array_each(arr, f)`. The callback's instructions count against the same instance budget.
  A callback that yields raises the existing "cannot yield" error.
- **Done when.** There are goldens for each function, for an error raised inside the callback and
  rescued outside, for budget exhaustion inside a callback, and for a function value from another
  instance (Q-1 `TypeError`).

### C6 — Map type as a native library
- **Size** L · **Format bump** no
- **Owner decision.** Is a native API without new syntax enough for now? A `{k: v}` literal would
  need lexer work, since `{` is reserved for interpolation, so it is left out of this task.
- **Scope.** An insertion-ordered map whose keys are numbers or text only. API: `Map_new()`,
  `Map_set`, `Map_get` (`null` when missing), `Map_has`, `Map_remove`, `Map_keys`, `Map_values`,
  `Map_length`. A map must be a first-class runtime value, not an opaque `NativeObject`, so that it
  is formatted canonically (`Map{k: v, ...}`), hashed by `ComputeStateHash()` (a new marker byte,
  with cycle and back-reference handling as for arrays), counted against the memory budget, and
  handled by `==` (identity) and by the cross-instance rule (Q-1).
- **Done when.** There are goldens for each function, iteration order, formatting, a state-hash
  pin, a cyclic map (a map containing itself) that formats and hashes without hanging, budget
  limits, and `foreach` over `Map_keys`.

---

## D. Performance

Every D task attaches before and after numbers from `dotnet test --filter TestCategory=Benchmark`
(same machine, Release build) to its PR. If the gain is under about 3%, the PR says so and the
owner decides whether to merge.

### D1 — `Chunk.Code` as `byte[]`
- **Size** M · **Format bump** no · **Owner decision** no · **Collides with** B3, D4, D7
- **Why.** F-PERF-4: measured as 10 to 14% faster on loops.
- **Scope.** `Chunk.Code` (`Runtime/Bytecode/Chunk.cs:17`) stays a growable builder during
  compilation and is frozen to `byte[]` when the function is finished. The VM (`ReadU16`,
  `DoHandleException`'s `ref List<byte>`), the verifier, the serializer and the disassembler read
  the array. The line table may follow if it's trivial.
- **Done when.** No `List<byte>` remains on the VM's read path, tests pass, and benchmarks are
  attached.

### D2 — Smaller per-instance footprint
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** F-VM-1: a trivial instance costs about 178 KB, 135 KB of it the `StringInterner`
  pre-sized to 4,096 entries, and a map holds dozens of instances.
- **Scope.** Create the interner lazily with a small initial capacity. Start the VM stack small and
  let it grow up to the existing limit. Determinism is unaffected, because limits are unchanged and
  only capacity changes.
- **Done when.** An allocation test pins the trivial-instance footprint (the plan's B.4 method)
  below an agreed figure, and stack-overflow goldens still report at the same depth.

### D3 — Opcode-pair histogram
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** This produces the data for choosing superinstructions (F-PERF-5). It is useful on its
  own; D7 does not wait for it.
- **Scope.** A debug-only counter, compiled out or behind a flag that's off by default with zero
  cost when off, that records opcode pairs and triples executed. Add a `Headless~/Runner` option
  that prints the top 30 for a script set.
- **Done when.** Running it over the goldens and (by the owner) the warman mod produces a table,
  which is committed to `Documentation~/perf/opcode-pairs.md`.

### D4 — Allocation-free native calls
- **Size** L · **Format bump** no · **Owner decision** no (R4 allows ABI breaks; the game
  regenerates its bindings) · **Collides with** D1, and every C task in flight. If a C task merges
  first, D4 converts its natives too. If D4 merges first, the C task writes against the new signature.
- **Why.** F-PERF-2: `CollectArgs` (`WarVM.cs:1935`) allocates a `List<WarValue>` on every native
  call.
- **Scope.** A custom delegate, `delegate WarValue NativeFn(ReadOnlySpan<WarValue> args)` (a ref
  struct cannot be a generic argument), replaces `Func<List<WarValue>, WarValue>` in
  `NativeFunctionDefinition`. The `NativeHelper` accessors take the span. All built-in libraries and
  the `Editor/WsBindingGenerator.cs` template are updated. Arguments are a slice of the VM stack and
  must not outlive the call, so a native that stores them must copy them. Document that.
- **Done when.** `AllocationGuardTests` asserts zero bytes per native call. The binding generator
  check (`.claude-task/tools/check-binding-generator.sh`) passes. `README.md`'s "add a native"
  section and `CLAUDE.md` are updated. Benchmarks are attached.

### D5 — Call-site inline caches
- **Size** L · **Format bump** no · **Owner decision** no · **Collides with** B3, E5
- **Why.** F-PERF-1: the biggest item for event-glue workloads. Every `Call` hashes `(name, arity)`
  up the scope chain, and every `CallMethod` walks `FindClassWithMethod`.
- **Scope.** Cache the resolved definition per call site in a side table, like the existing
  property inline caches (`InlineCachesOf`), so the bytecode is unchanged. Invalidate with
  `DefinitionsVersion`. The cache is per instance, which keeps F-VM-3 (shared programs) possible.
  Method calls cache by receiver class.
- **Done when.** There are tests for cache hit, `Reload` invalidation, polymorphic method sites
  (they must still be correct), and imported functions. Benchmarks are attached for a call-heavy
  script.

### D6 — `ConcatN` for string interpolation
- **Size** M · **Format bump** yes · **Owner decision** no
- **Why.** F-PERF-3: 20,000 interpolations with 3 holes allocate 7.5 MB, building every
  intermediate string.
- **Scope.** A `ConcatN n` opcode, with its entry in the opcode-info table and the verifier, that
  builds the result in one allocation with a single `MaxTextLength` check. Interpolation desugars to
  it. Remove interning from the `Add` path, or make it opt-in.
- **Done when.** Goldens and the disassembly golden are updated, the format version is bumped, and
  an allocation test shows one string per interpolation.

### D7 — `StoreLocal` superinstruction
- **Size** M · **Format bump** yes · **Owner decision** no
- **Why.** F-PERF-5: `SetLocal` + `Pop` is the obvious candidate in every assignment statement.
- **Scope.** One superinstruction, emitted by the compiler (not a peephole pass over finished code)
  for an assignment statement whose value is discarded. Add the verifier entry, the disassembler
  name and the serializer handling. Any further superinstructions are separate tasks, chosen from
  D3's data.
- **Done when.** There's a disassembly golden showing it, the instruction budget counts it as one
  instruction (document that in `LANGUAGE.md` section 12), the format is bumped, and benchmarks are
  attached. If `errors/instruction_budget.ws` or the budget tests change their reported line or
  count, the PR explains why.

### D8 — `Dup2`: compound assignment evaluates its target once
- **Size** M · **Format bump** yes · **Owner decision** no (the v2 plan already plans to lift
  PROP-32)
- **Why.** F-PERF-8. It also removes a language restriction: `g()[0] += 1` is currently a compile
  error.
- **Scope.** A `Dup2` opcode. Compound assignment evaluates `T`/`i` once and duplicates them. Remove
  `CheckCompoundTarget` and its error.
- **Done when.** Goldens show the side effect running exactly once (`g()` prints once). The
  restriction is removed from `README.md` gotchas, `LANGUAGE.md` and `CLAUDE.md`. The format is
  bumped.

---

## E. Language

Each E task updates `LANGUAGE.md` and the README language guide, and adds goldens under
`Tests/Golden~/language/`.

### E1 — `@method()` sugar
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** F-LANG-4: `@x` already means `this.x`, so `@m(args)` meaning `this.m(args)` is the
  natural extension.
- **Scope.** Lexer and parser only. Desugar to the existing method call. It is a compile error
  outside a class, with the same message as `@x`.
- **Done when.** There are goldens for the call, for inheritance (it dispatches like `this.m()`),
  and for use outside a class.

### E2 — Structured errors in `rescue`
- **Size** M · **Format bump** maybe
- **Owner decision.** `rescue e` currently binds the message text
  (`errors/rescue_value_is_message.ws`), and the warman mod relies on that. Options:
  1. `e` stays text, and a new native `error_kind()` (valid inside a rescue body) returns
     `"IndexError"` and so on.
  2. `e` becomes an `Error` instance with `.kind`, `.message`, `.line` and `.stack`, and
     `"" + e` formats as the message.
  3. A second binding: `rescue e, kind`.

  The suggestion is option 2, with a format that keeps `print "{e}"` output unchanged. Option 1 is
  the zero-risk choice.
- **Done when.** The chosen design is implemented with goldens for every `ScriptErrorKind`,
  re-raising keeps the kind (`errors/ensure_reraise_keeps_kind.ws` still passes), and `LANGUAGE.md`
  section 9 documents the new form. If the owner chose option 2, run the warman mod (B7, option 1)
  to check that its `rescue` output is unchanged.

### E3 — Numeric overflow detection
- **Size** M · **Format bump** no
- **Owner decision.** F-LANG-3: `F64` `+` and `-` wrap silently, `*` is unchecked, and `/`
  saturates. Should overflow raise an `ArithmeticError`, always or only in a debug mode? (A debug
  mode means behavior differs between modes, which is risky for lockstep if peers disagree.) The
  suggestion is to always raise. It is deterministic and catches real bugs.
- **Scope.** Checked add, subtract and multiply in the VM's numeric ops and in compile-time
  constant folding, if any. Negation of `MinValue`. Decide what `/` saturation does under the
  same rule.
- **Done when.** There are boundary goldens (`FixedPointBoundaryTests` is extended), benchmarks
  show the arithmetic cost, and `LANGUAGE.md` section 3 is updated.

### E4 — Lambdas capture locals by value
- **Size** L · **Format bump** likely
- **Owner decision.** Is capture by value (a snapshot when the lambda is created) acceptable?
  Capture by reference (true upvalues) is a much bigger change. By value covers the common
  "callback with context" case. Writing to a captured name inside the lambda stays a compile error.
- **Scope.** Change the compile error for reading an outer local into a capture. A lambda value
  carries its captured slots, and they are included in formatting (if function values format) and
  in `ComputeStateHash()` if lambdas can be stored in globals. Serializer support follows if the
  format changes.
- **Done when.** There are goldens for capture in a loop (each iteration gets its own snapshot), a
  nested lambda, and a write to a captured name (compile error). The README gotcha "Lambdas have no
  closures" is updated.

### E5 — Call any expression
- **Size** M · **Format bump** only if no existing "call value on the stack" opcode fits
- **Owner decision** no
- **Why.** `arr[0](x)` and `f(x)(y)` are syntax errors today ("only a name can be called").
- **Scope.** The parser accepts a call after any complete operand. The compiler emits a call of the
  value on the stack. A non-function value raises the existing "not callable" `TypeError`.
- **Done when.** There are goldens for each form and for the error. The README gotcha is removed.

---

## F. Tooling

### F1 — VS Code syntax highlighting
- **Size** S · **Format bump** no · **Owner decision** no
- **Scope.** `Tools~/vscode-warscript/`: a TextMate grammar for keywords, `end` blocks, `fun`,
  `class`, `@field`, interpolation `{...}` inside strings, raw `"""` strings, numbers with `_`, and
  comments. Include a language configuration (brackets, comment toggling, `end` indentation) and a
  README describing local installation (`code --install-extension` from a `.vsix`).
- **Done when.** A screenshot of a representative golden file is in the PR. Publishing to the
  marketplace is the owner's call and not part of the task.

### F2 — `wsc` command-line tool
- **Size** M · **Format bump** no · **Owner decision** no
- **Why.** F-TOOL-1. It lets modders and CI check scripts without Unity.
- **Scope.** Extend `Headless~/Runner` (or add `Headless~/Wsc`) with these commands:
  `check <files>` (diagnostics, optionally against a `.wsmanifest`), `compile` (write bytecode),
  `disasm`, `run` (print output and the `ScriptResult`), and `hash`. Exit codes are non-zero on
  errors. Diagnostics use the `file:line:col: severity: message` format so editors can parse them.
- **Done when.** There's a test per command (`check` is tested against the committed warman
  manifest), and the README has a usage section.

### F3 — Diagnostics-only language server
- **Size** L · **Format bump** no · **Owner decision** no
- **Why.** F-TOOL-3, reduced to what can ship in one PR: errors and warnings as you type.
  Go-to-definition and completion are separate later tasks.
- **Scope.** `Tools~/WarScript.Lsp/`: a console LSP server over stdio that, on open and change,
  compiles the document (resolving imports from the workspace and loading an optional
  `.wsmanifest`) and publishes `CompileResult.Diagnostics` with line and column. Wire it into F1's
  extension if F1 has landed. Otherwise document a generic LSP client setup.
- **Done when.** There is an end-to-end test that sends `didOpen` with a script containing an error
  and receives the diagnostic at the right position.

---

## G. Testing

### G1 — Coverage-guided fuzzing with SharpFuzz
- **Size** M · **Format bump** no · **Owner decision** no
- **Why.** F-TEST-1. The random fuzzer (T0.7) already found four classes of bug.
- **Scope.** A SharpFuzz harness in `Headless~/Fuzz/` that feeds input to `Compile()` and `Run()`
  with small budgets and the same oracle as the existing fuzzer: no C# exception escapes, and every
  result is `Ok` or `Error`. Seed it from the golden corpus, and also add a bytecode-loader target.
  Add a script that runs it locally with AFL or libFuzzer.
- **Done when.** There's a documented one-hour run. Every finding is fixed with a golden, or
  recorded as a new task in this file.

### G2 — Mutation-testing report with Stryker.NET
- **Size** S · **Format bump** no · **Owner decision** no
- **Why.** F-TEST-2. It shows which of the roughly 1,450 tests protect nothing.
- **Scope.** A Stryker config for the headless test project, scoped to `Runtime/`. Run it, then
  commit the summary to `Documentation~/perf/mutation-report.md` (score per file, top 20 surviving
  mutants). No code changes. Each weak area becomes a new task in this file.
- **Done when.** The report is committed, and new test tasks are added under a new "H" section if
  warranted.
