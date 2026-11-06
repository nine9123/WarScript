# Recording a behavior baseline (T0.9)

Phase 3 changes the language's semantics (v2 plan, section 5), and the game's
scripts are migrated to it (section 8). To show that migrated scenarios still
behave as before, apart from documented semantic changes, the owner records a
baseline **with the current v1 runtime, before Phase 3 starts**, and compares
against it after the migration (plan 8.3, step 7).

Claude Code cannot run the game, so this is an owner step. This file explains
what to record and how.

## Where the baselines are

| | |
|---|---|
| Location | *Not recorded yet. When the baselines exist, put their location here (plan T0.9, "Done when").* |
| Recorded with | *WarScript commit (`git rev-parse HEAD` of the package) and game build* |
| Date | |

## What a baseline is

For each **scenario** (a reproducible game session: fixed inputs, fixed seeds,
fixed tick rate), and for each **WarScript instance** in it, record:

1. **The printed output**: every line the instance's logger receives from
   `print`, in order.
2. **A per-tick state hash**: after each simulation tick, once every script
   call for that tick has returned, the value of
   `WarScriptLanguage.ComputeStateHash()`.

`ComputeStateHash()` (plan 9.7) is a 64-bit FNV-1a hash of everything the
instance has printed since it was created and of the current values of its
user globals. Host globals such as `[WsConst]` values are excluded. It is
cumulative, so the first tick whose hash differs is the first tick where the
two runs diverged.

Suggested scenarios (Q-7, Q-10): loading the core mod, room setup, a match
that exercises skills and modifiers, and any scenario that has produced a
desync in the past. More short scenarios are better than one long one: a
difference is easier to trace.

## How to record

Wrap each instance's logger and record the hash at the end of every tick:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WarScript;

/// Records one instance's printed lines and per-tick state hashes.
public sealed class BaselineRecorder
{
    private readonly List<string> _output = new List<string>();
    private readonly List<string> _hashes = new List<string>();

    /// Pass this as the WarScriptLanguage logger, or call it from your own logger.
    /// Note: v1 also logs the stack trace of an unhandled error through the
    /// logger; it is recorded too, which is fine for a baseline.
    public void Log(WarScriptLanguage script, string line) => _output.Add(line);

    /// Call once per simulation tick, after every script call of that tick.
    public void EndTick(long tick, WarScriptLanguage script) =>
        _hashes.Add(tick.ToString(CultureInfo.InvariantCulture) + ": " +
                    script.ComputeStateHash().ToString("x16", CultureInfo.InvariantCulture));

    /// Writes <label>.output.txt and <label>.hashes.txt into the scenario folder.
    public void Save(string scenarioDirectory, string instanceLabel)
    {
        Directory.CreateDirectory(scenarioDirectory);
        File.WriteAllLines(Path.Combine(scenarioDirectory, instanceLabel + ".output.txt"), _output);
        File.WriteAllLines(Path.Combine(scenarioDirectory, instanceLabel + ".hashes.txt"), _hashes);
    }
}
```

Suggested layout, one folder per scenario:

```
<baseline root>/
  README.txt                    WarScript commit, game build, date, how each scenario is started
  core-mod-load/
    core.output.txt
    core.hashes.txt             "<tick>: <16 hex digits>" per line
  match-skills-modifiers/
    core.output.txt
    core.hashes.txt
    room.output.txt
    room.hashes.txt
```

The instance label must be stable between runs (for example the mod or
script name), not an object hash or a creation counter that depends on
timing.

### Make the run reproducible first

- Run each scenario **twice** with the v1 runtime and check that both runs
  produce identical files. If they do not, the scenario is not reproducible
  (input timing, seeds, wall-clock use on the host side), and comparing it
  after the migration will not tell you anything. Fix that first.
- Set the simulation thread's culture to `CultureInfo.InvariantCulture` while
  recording. v1 formats negative numbers with the culture's minus sign
  (DET-1, fixed in T1.6), so a baseline recorded under, say, sv-SE would not
  match one recorded under en-US.
- Record on the platform the owner develops on. Cross-platform comparison is a
  separate check (plan 9.8).

## Comparing after the migration

Re-run each scenario on the migrated scripts with the v2 runtime, record the
same files, and compare them (`diff`, or `Headless~/compare-hashes.sh`, which
diffs any two line-oriented files).

Some differences are expected. Each one must be traced to a documented
semantic change, otherwise it is a migration miss or a v2 bug (plan 8.3,
step 7):

| Change | Effect on the recorded files |
|---|---|
| Booleans format as `true`/`false` (DEC-11, T3.6) | Output lines that print a logical value; every hash from that tick on |
| Culture-invariant, integer-only number formatting (T1.6) | Fractional numbers with no short decimal form, and `F64Vec3` values, format differently; this shows up in output and, through text globals, in hashes. Only applies if the baseline is compared across T1.6 |
| Instances hashed by declared field order (T3.4, plan 9.7 notes) | Hashes of any state holding a class instance change even when behavior does not. Compare those scenarios by output, or by hashes recorded after T3.4 |
| Functions no longer write globals implicitly (DEC-5, T3.4) | Globals that a function used to overwrite keep their value, which changes hashes. A migrated script that inserted `global` behaves as before |
| Class instances compare by identity (DEC-9, T3.6) | Branches on `==` between distinct instances with equal fields go the other way |
| Errors that v1 ignored now raise (DEC-8, DEC-10, T3.6) | New error traces in the output where a script relied on a silent failure |

When hashes differ, find the first differing tick, then compare the printed
output up to that tick. Adding temporary `print` statements to the scenario's
scripts on both versions narrows it further.
