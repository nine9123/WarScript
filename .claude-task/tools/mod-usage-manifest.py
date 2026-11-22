#!/usr/bin/env python3
"""Builds a *provisional* native manifest (PROP-30) from how a mod calls natives (T3.9).

Usage: mod-usage-manifest.py <mod folder> <manifest out> [<snippets.json out>]

Only for checking a mod while the game's own WarScriptNatives.wsmanifest
(written by Generate Bindings) is not at hand: a native's arities are the ones
the mod uses, named parameters are the names it passes, positional ones are
p0, p1, ...; every called name the mod does not define is taken for a native.
A clean compile against it (check-mod.sh) proves syntax, import order, v2
scoping and every compile check except the natives' real signatures. Scripts
held as text (attach_script CustomText in *.wroom.json, npc_add_script_option
arguments) are scanned too."""
import json, re, sys, pathlib

root = pathlib.Path(sys.argv[1])
defined = set()          # functions the mod defines itself
calls = {}               # name -> list of (positional count, [named])

def scan(src):
    i, n = 0, len(src)
    out = []             # tokens: ('id', name, pos) / ('str',) / ('p', ch)
    while i < n:
        c = src[i]
        if c == '#':
            while i < n and src[i] != '\n': i += 1
        elif c == '"':
            i += 1
            while i < n and src[i] != '"':
                i += 2 if src[i] == '\\' else 1
            i += 1; out.append(('str',))
        elif c.isalpha() or c == '_':
            j = i
            while j < n and (src[j].isalnum() or src[j] == '_'): j += 1
            out.append(('id', src[i:j])); i = j
        elif c.isdigit() or (c == '-' and i + 1 < n and src[i+1].isdigit()):
            j = i + 1
            while j < n and (src[j].isdigit() or src[j] == '.'): j += 1
            out.append(('num',)); i = j
        elif c in '[]{}(),:':
            out.append(('p', c)); i += 1
        elif c == '\n':
            out.append(('nl',)); i += 1
        else:
            i += 1
    return out

def analyse(src):
    t = scan(src)
    for k, tok in enumerate(t):
        if tok[0] != 'id': continue
        if k > 0 and t[k-1] == ('id', 'fun'):
            defined.add(tok[1]); continue
        if k + 1 < len(t) and t[k+1] == ('p', '['):
            depth, args, named, cur_empty = 0, 0, [], True
            m = k + 1
            while m < len(t):
                x = t[m]
                if x[0] == 'p' and x[1] in '[{(':
                    depth += 1
                    if depth > 1: cur_empty = False
                elif x[0] == 'p' and x[1] in ']})':
                    depth -= 1
                    if depth == 0: break
                elif depth == 1 and x == ('p', ','):
                    args += 1; cur_empty = True
                elif depth == 1 and x[0] == 'id' and m + 1 < len(t) and t[m+1] == ('p', ':') and cur_empty:
                    named.append(x[1]); cur_empty = False
                elif x[0] != 'nl':
                    cur_empty = False
                m += 1
            total = 0 if (args == 0 and cur_empty) else args + 1
            calls.setdefault(tok[1], []).append((total - len(named), named))

for f in sorted(root.rglob('*.ws')):
    analyse(f.read_text(encoding='utf-8'))
# Scripts held as text: attach_script objects in room files, and script options.
snippets = []
for f in sorted(root.rglob('*.wroom.json')):
    def walk(o):
        if isinstance(o, dict):
            if o.get('doodad') == 'attach_script':
                for inst in o.get('instances', []):
                    s = inst.get('config', {}).get('CustomText')
                    if s: snippets.append((str(f.relative_to(root)), inst['config'].get('Id'), s))
            for v in o.values(): walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(json.loads(f.read_text(encoding='utf-8')))
for f in sorted(root.rglob('*.ws')):
    # The same pattern as check-mod.sh, the argument's escapes decoded.
    for m in re.finditer(r'npc_add_script_option\s*\[[^\]\n]*?"((?:[^"\\\n]|\\.)*)"\s*\]', f.read_text(encoding='utf-8')):
        snippets.append((str(f.relative_to(root)), 'npc_add_script_option', json.loads('"' + m.group(1) + '"')))
for _, _, s in snippets:
    analyse(s)

lines = ['# PROVISIONAL manifest inferred from the warman mod\'s own calls (T3.9).',
         '# Replace with the game\'s WarScriptNatives.wsmanifest (Generate Bindings).',
         'warscript-manifest 2']
for name in sorted(calls):
    if name in defined or name == 'print': continue
    uses = calls[name]
    named_order = []
    for _, nm in uses:
        for x in nm:
            if x not in named_order: named_order.append(x)
    maxpos = max(p for p, _ in uses)
    minpos = min(p + len(nm) for p, nm in uses)
    params = [f'p{i}' for i in range(maxpos)] + named_order
    lines.append(f'function {name} {min(minpos, len(params))} ' + ' '.join(params))
pathlib.Path(sys.argv[2]).write_text('\n'.join(lines).replace(' \n', '\n') + '\n')
if len(sys.argv) > 3:
    json.dump(snippets, open(sys.argv[3], 'w'), ensure_ascii=False, indent=1)
print(len(calls), 'called names;', len(snippets), 'snippets')
