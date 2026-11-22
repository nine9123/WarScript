#!/usr/bin/env bash
# Plan 8.3 step 7 / T3.9: compiles every .ws file of a mod with the v2
# compiler against a native manifest (PROP-30), warnings as errors (PROP-31),
# resolving imports from the mod's root as the game does. Also compiles the
# scripts a warman-style mod holds as text: the CustomText of every
# attach_script object in its *.wroom.json files, and the script argument of
# npc_add_script_option[...] calls. Nothing runs.
#
# Usage: .claude-task/tools/check-mod.sh <mod folder> <manifest>
# Prints one FAIL line (the runner's JSON) per script that does not compile
# clean, then "<n> scripts compiled, <m> failed"; exits 1 when any failed.
# Needs the runner built: dotnet build -c Release "Headless~/Runner".
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RUNNER="${RUNNER:-$ROOT/Headless~/Runner/bin/Release/net8.0/WarScript.Runner.dll}"
MOD="$(cd "$1" && pwd)"; MANIFEST="$(realpath "$2")"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
fail=0; n=0
check() {  # <file> <label>
  n=$((n + 1))
  local out status
  out="$(dotnet "$RUNNER" "$1" --compile-only --warnings-as-errors --manifest "$MANIFEST" --import-root "$MOD")"
  status="$(python3 -c 'import json,sys; j=json.loads(sys.argv[1]); print(j["status"], len(j.get("warnings") or []))' "$out")"
  if [ "$status" != "ok 0" ]; then fail=$((fail + 1)); echo "FAIL $2: $out"; fi
}
while IFS= read -r f; do check "$f" "${f#"$MOD"/}"; done < <(find "$MOD" -name '*.ws' | LC_ALL=C sort)
python3 - "$MOD" "$TMP" <<'PY'
import json, pathlib, re, sys
mod, tmp = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
found = []
def walk(o, where):
    if isinstance(o, dict):
        if o.get('doodad') == 'attach_script':
            for inst in o.get('instances', []):
                text = inst.get('config', {}).get('CustomText')
                if text: found.append((f"{where} attach_script id {inst['config'].get('Id')}", text))
        for v in o.values(): walk(v, where)
    elif isinstance(o, list):
        for v in o: walk(v, where)
for f in sorted(mod.rglob('*.wroom.json')):
    walk(json.loads(f.read_text(encoding='utf-8')), str(f.relative_to(mod)))
for f in sorted(mod.rglob('*.ws')):
    for m in re.finditer(r'npc_add_script_option\s*\[[^\]\n]*?"((?:[^"\\\n]|\\.)*)"\s*\]', f.read_text(encoding='utf-8')):
        found.append((f"{f.relative_to(mod)} npc_add_script_option", json.loads('"' + m.group(1) + '"')))
for i, (label, text) in enumerate(found):
    (tmp / f"snippet_{i}.ws").write_text(text + "\n", encoding='utf-8')
    (tmp / f"snippet_{i}.label").write_text(f"{label}: {text}", encoding='utf-8')
PY
for s in "$TMP"/snippet_*.ws; do [ -e "$s" ] && check "$s" "$(cat "${s%.ws}.label")"; done
echo "$n scripts compiled, $fail failed"
[ "$fail" -eq 0 ]
