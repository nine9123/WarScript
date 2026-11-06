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

A first recording, `run-1`, was received on 2026-11-06. It is not usable as
the baseline yet; see [Review of run-1](#review-of-run-1) for why and what to
record next.

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

    /// Writes <label>.output.txt, <label>.hashes.txt and <label>.globals.txt
    /// into the scenario folder. Call it at the end of the scenario.
    public void Save(string scenarioDirectory, string instanceLabel, WarScriptLanguage script)
    {
        Directory.CreateDirectory(scenarioDirectory);
        File.WriteAllLines(Path.Combine(scenarioDirectory, instanceLabel + ".output.txt"), _output);
        File.WriteAllLines(Path.Combine(scenarioDirectory, instanceLabel + ".hashes.txt"), _hashes);
        File.WriteAllLines(Path.Combine(scenarioDirectory, instanceLabel + ".globals.txt"), new[]
        {
            "user: " + Names(script.UserMemoryScope),   // covered by the hash
            "host: " + Names(script.GlobalMemoryScope), // not covered by the hash
        });
    }

    private static string Names(WarScript.Context.MemoryScope scope)
    {
        var names = new List<string>(scope.GetAllLocals().Keys);
        names.Sort(System.StringComparer.Ordinal);
        return string.Join(", ", names);
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
    core.globals.txt            names of the user and host globals at the end
  match-skills-modifiers/
    core.output.txt
    core.hashes.txt
    room.output.txt
    room.hashes.txt
```

The instance label must be stable between runs (for example the mod or
script name), not an object hash or a creation counter that depends on
timing. A suffix such as `#2` for the second instance attached at the same
place is fine as long as the order in which the instances are created is
itself deterministic.

### Check that the hash sees the scripts' state

`ComputeStateHash()` only covers what the instance printed and its **user
globals**: the variables a script's top level assigns. It does not cover
function or class definitions, host globals (`GlobalMemoryScope`, including a
host-set name that a script later assigns to: v1 updates the existing host
variable instead of creating a user one), or state the scripts keep in host
objects through native calls. If an instance has no user globals, its hash
only changes when it prints.

`<label>.globals.txt` shows which globals the hash covered at the end of the
scenario. A hash that equals one of these values also means "no user globals":

| Hash | State |
|---|---|
| `af64724c8602eb6e` | nothing printed, no user globals |
| `dc553fa85c69d526` | printed `bootstrap`, no user globals |

(both pinned by `StateHashTests.StateWithoutGlobalsHashesOnlyThePrintedLines`).
If most instances of a scenario hash like this, the baseline compares little
more than the printed output. Make the scenario's script behavior visible in
one of these ways, on the v1 runtime, before recording:

- add `print` lines at the points that matter (a room was set up, a skill
  hit, a modifier was applied, with the values involved), or
- have the host's own tick hash (the game state that the scripts drive
  through native calls) recorded next to the WarScript hash, so that a
  difference caused by a script shows up even when the script holds no
  globals.

Temporary `print` lines added for the baseline must stay in the scripts until
the comparison after the migration, so that both sides print the same.

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

## Review of run-1

`run-1` (received 2026-11-06, recorded on macOS) holds two scenarios with the
layout above:

| Scenario | Instance | Ticks | Distinct hashes | Output |
|---|---|---|---|---|
| general-gameplay | `bootstrap.ws` | load | 1 (`dc553fa85c69d526`) | `bootstrap` |
| general-gameplay | `resources.ws` | load | 1 (`af64724c8602eb6e`) | none |
| general-gameplay | `room island r0 rooms_island.ws` | 0–1037 | 1 (`a81202f32ab1baad`) | `island.ws` |
| general-gameplay | `room island r0 AttachScript ((60, 29, 53))` | 254–1037 | 1 (`af64724c8602eb6e`) | none |
| more-gameplay | `bootstrap.ws` | load | 1 (`dc553fa85c69d526`) | `bootstrap` |
| more-gameplay | `resources.ws` | load | 1 (`af64724c8602eb6e`) | none |
| more-gameplay | `room island r0 rooms_island.ws` | 0–2183 | 1 (`a81202f32ab1baad`) | `island.ws` |
| more-gameplay | `room lower_prison_west r2 rooms_outpost_1_outpost-1.ws` | 560–2183 | 1 (`af64724c8602eb6e`) | none |
| more-gameplay | `room lower_prison_west r2 AttachScript ((18, 332, 350.5))` | 857–2183 | 1 (`af64724c8602eb6e`) | none |
| more-gameplay | `room lower_prison_west r2 AttachScript ((18, 332, 350.5))#2` | 1680–2183 | 1 (`af64724c8602eb6e`) | none |

The files are well-formed: one `<tick>: <hash>` line per tick, no gaps in
the tick ranges, and the two scenarios agree on the ticks they share
(`rooms_island.ws`, ticks 0–1037). No instance logged an error.

**Finding: the hashes carry no script state.** Every hash in the recording
equals the hash of the instance's printed lines with **no user globals**
(`a81202f32ab1baad` is "printed `island.ws`, no user globals"; the other two
values are in the table in [Check that the hash sees the scripts'
state](#check-that-the-hash-sees-the-scripts-state)). No hash changes over
any instance's lifetime. So, as recorded, the baseline compares three printed
lines and nothing else; a migration that changed what these scripts do would
very likely still match it. The cause is on the game side and cannot be
determined from the recording: most likely these scripts keep no top-level
variables and act only through native calls, or they assign to names the
host set up (which v1 stores as host globals).

**Also missing:**
- A second run of each scenario to show it is reproducible
  ([Make the run reproducible first](#make-the-run-reproducible-first)).
  Both scenarios agreeing on their shared ticks is consistent with that, but
  with constant hashes it shows nothing.
- `README.txt` with the WarScript commit, game build and how each scenario
  is started.
- The scenarios suggested above that exercise skills and modifiers; run-1
  covers loading and two rooms.

**Next recording (run-2).** Use the updated `BaselineRecorder`, which also
writes `<label>.globals.txt`, add `print` lines or a host-side hash as
described in [Check that the hash sees the scripts'
state](#check-that-the-hash-sees-the-scripts-state), record each scenario
twice, and add `README.txt`. When the result is accepted, fill in [Where the
baselines are](#where-the-baselines-are) and mark T0.9 done in the plan
tracker.
