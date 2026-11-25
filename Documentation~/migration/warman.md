# Migrating the warman mod (T3.9)

Plan 8.3 steps 3 to 7 for the game's scripts: the warman mod as received on
2026-11-21 (166 `.ws` files, 8 languages of translations, maps, sounds, jobs,
quests, doodads and two room scripts).

## What v2 changed for the mod

The mod's scripts only call natives: no assignment, class, loop or variable
anywhere (see BASELINE.md, "Headless trace of the warman mod"). So none of the
scoping, field, operator or equality rules of v2 reach them. One rule does:
imports come before every other statement of a file, and a file's imports run
before its own code, in the order listed (5.8, T3.3, T3.7).

Each `translation/<lang>.ws` called `translation_create["..."]` and then
imported its 15 block files, whose `t[...]` calls add to the translation just
created. v1 ran an import where it stood; v2 rejects the file
(`'import' must come before every other statement of the file`), and moving
the imports up would run every `t[...]` before `translation_create`.

`WarScript.Migrate --report` lists exactly these 120 imports as `import-order`
items (`migration-report-warman.md`, run with `--import-root` on the mod) and
nothing else.

## The change

| File | Change |
|---|---|
| `translation/<lang>/create.ws` (8 new, each with a `.meta`) | Holds the language's `translation_create[...]` call |
| `translation/<lang>.ws` (8) | Imports only: `create.ws` first, then the 15 block files in their old order |
| `translation/<lang>/*.ws` (120) | Header comment: "Imported by translation/<lang>.ws, after translation/<lang>/create.ws." |

Since `resources.ws` imports the languages in the same order as before and v2
runs each file's imports depth-first in listed order, the native calls happen
in v1's order.

## Checks

* **Compile** (`.claude-task/tools/check-mod.sh warman WarScriptNatives.wsmanifest`):
  every `.ws` file (174) and the 4 scripts held as text (attach_script
  `CustomText` in `rooms/island.wroom.json` and
  `rooms/outpost/1/lower_prison_west.wroom.json`, the script argument of
  `npc_add_script_option` in `rooms/island.ws`) compile against the game's
  native manifest with warnings as errors and imports resolved from the mod
  root: 178 clean. The unmigrated mod: 9 compile errors (the 8 language files,
  and `resources.ws` through them). The manifest is the one Generate Bindings
  wrote on the owner's side on 2026-11-22 (every native is registered through
  `[WsModule]`), kept as `warman.WarScriptNatives.wsmanifest`; it declares 140
  natives, including every one the mod and its text scripts call. That it was
  in effect was checked with a script calling `t` with one argument
  (`Function 't' takes 2 arguments, not 1`) and `sound_define` with a
  misspelt named argument (`Function 'sound_define' has no parameter 'volum'`).
* **Native-call order** (`Headless~/ModTrace`, v2 runtime): `resources.ws`
  makes 9,488 calls, the same sequence (name and first text argument) as v1's
  import-where-reached order over the original files.
  `bootstrap.ws` (`3eee18c4234a6e5e`), `rooms/island.ws` (`dcbb6dcef292b4ef`)
  and `rooms/outpost/1/outpost-1.ws` (`e597db43fbc802ac`) have the trace
  hashes of the 2026-11-06 baseline.

## What is left (owner)

1. Play. A runtime error is a v2 error with a kind, a line and a stack (T2.5).
2. Plan 8.3 step 8: regenerate the mod archives (bytecode format v3, T3.11),
   then remove `Tools~/WarScript.Migrate` and the audit hooks.

After a change to the game's natives or the mod, re-run Generate Bindings and
`.claude-task/tools/check-mod.sh <mod folder> WarScriptNatives.wsmanifest`
(build the runner first: `dotnet build -c Release "Headless~/Runner"`).
`.claude-task/tools/mod-usage-manifest.py` writes a provisional manifest from a
mod's own calls, for checking where the game's manifest is not at hand; it
cannot catch a wrong arity or parameter name (the first compile of this
migration used one, before the game's manifest arrived).
