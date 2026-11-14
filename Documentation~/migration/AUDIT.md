# Audit mode: recording v1 behaviors that change in v2 (T0.8)

> **Game sessions are optional since 2026-11-12 (plan DEC-20).** The migration
> uses only the test-suite report; nothing waits for a game-session report. This
> file stays for anyone who wants to record one anyway.

Phase 3 changes what some v1 code does (v2 plan, section 5): mixed-type
equality, text subtraction, silently ignored index writes, functions writing
globals, and so on. Some of those uses can be found by reading the source
(the migration tool, T3.2); the rest only show up while the scripts run. Audit
mode records them from the running v1 runtime, so the migration review (plan
8.3, step 4) knows which scripts rely on behavior that is about to change.

There are two reports:

| Report | Who records it | Where |
|---|---|---|
| Test suite | Claude Code, with the headless suite | [`audit-tests.md`](audit-tests.md) |
| Game sessions | The owner (Q-7, Q-10) | Wherever the owner prefers; note the location below |

## The API

`WarScriptLanguage.AuditSink` (`Action<AuditEvent>`, null by default) receives
one `AuditEvent` each time the VM takes one of the v1 paths listed in plan
8.1. An event has a `Kind` (`AuditKind`), the `Script` and `Line` it happened
at, the WarScript `Function` that was running, and a `Detail` with the values
involved, for example `number 1 == text "1" (v1: true)`.

`AuditReport` collects events from any number of instances (it is
thread-safe), groups them by (kind, script, line) with a count, and writes
Markdown (`ToMarkdown(title)`) or JSON (`ToJson()`).

With no sink attached, auditing costs one null check on the paths it covers;
with one attached, the run's output and state hash are unchanged
(`AuditTests.AuditingDoesNotChangeTheRun`).

Limits:

- Only bytecode execution is audited, not the legacy tree-walker (removed in T2.1).
- `Script` is the instance's script name or the import path the code was
  compiled from. Code loaded from bytecode (`LoadBytecode`, a
  `BytecodeResolver`) does not record its source, so its events carry the
  instance's script name.
- v1 records line 0 for many expression instructions (T1.10); such an event is
  reported at the line of the next instruction that has one, normally the
  statement the expression belongs to.

## Recording a game-session report (owner step)

1. Use WarScript at the commit that added audit mode or later, still v1 (before
   any Phase 3 change).
2. Create one report for the session and attach it to every
   `WarScriptLanguage` the game creates, before the instance's first `Run()`:

   ```csharp
   using WarScript.Audit;

   static readonly AuditReport Audit = new AuditReport();

   // wherever an instance is created:
   var script = new WarScriptLanguage(name, source, fileResolver, logger);
   script.AuditSink = Audit.Add;
   ```

   Give each instance a script name that identifies its file (for example the
   mod-relative path); the report keys sites by it.
3. Play the representative sessions: the core mod, room setup, skills and
   modifiers (plan T0.8). One report can span several sessions; it only counts.
4. When the sessions end, write the report:

   ```csharp
   File.WriteAllText("warscript-audit.md", Audit.ToMarkdown("WarScript audit: game sessions"));
   ```

   `ToJson()` writes the same data in a form that is easier to merge or filter.
5. Send the file back, or note where it is kept in the table below.

Audit mode never changes what a script does, so it can stay on for a whole
play session. A tick-heavy script can produce millions of events; the report
keeps one row per site, so its size depends on the number of distinct sites,
not on the event count.

## Game-session reports

| Recorded | WarScript commit | Sessions | Location |
|---|---|---|---|
| *none yet* | | | |

## Regenerating the test-suite report

```bash
WARSCRIPT_AUDIT_REPORT=$PWD/Documentation~/migration/audit-tests.md \
  dotnet test "Headless~/Tests/WarScript.Tests.csproj"
```

`Tests/AuditSuite.cs` attaches one report to every `TestHelper.Run` and to the
source-mode, invariant-culture run of every golden, and writes it when the
suite ends. The report is deterministic: regenerate it whenever goldens or
tests change before the migration review.
