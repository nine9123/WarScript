# ──────────────────────────────────────────────────────
# test_string_literals.ws
# Escape sequences, the $"..." interpolation prefix and
# raw """...""" literals — everything needed to carry a
# quote, a brace, or a whole WarScript snippet as a value
# ──────────────────────────────────────────────────────

# ── 1. Escaped quotes ──
q = "\""
assert "a\"b" == "a" + q + "b"
assert "she said \"hi\"" == "she said " + q + "hi" + q

# ── 2. Escaped backslash ──
assert "a\\b" - "\\" == "ab"
assert "\\" + "\\" == "\\\\"

# ── 3. Escaped braces suppress interpolation ──
name = "hero"
assert "\{name\}" != "hero"
assert "\{name\} vs {name}" == "\{name\} vs hero"

# ── 4. Control characters ──
two_lines = """
a
b
"""
assert two_lines == "a\nb"
assert "x\ty" != "xy"
assert "x\ty" == "x" + "\t" + "y"

# ── 5. $"..." is the explicit form of the same literal ──
n = 7
assert $"n = {n}" == "n = 7"
assert $"n = {n}" == "n = {n}"

# ── 6. Raw literals interpret nothing ──
assert """{name}""" == "\{name\}"
assert """a\nb""" == "a\\nb"

# ── 7. A raw literal may end in a quote — the run closes with its last three ──
assert """say "hi"""" == "say \"hi\""

# ── 8. A nested literal inside an interpolation keeps its braces ──
assert "result: {"a}b"}" == "result: a}b"
assert "{"{name}"}" == "hero"
assert "{"\""}" == q

# ── 9. The point of all this: a snippet carried as a value ──
fun option [label, action]
    return label + " -> " + action
end

snippet = """print "{greeting}, {party{0}}""""
assert snippet == "print \"\{greeting\}, \{party\{0\}\}\""
assert option ["Ask about the war", snippet] == "Ask about the war -> " + snippet

# ── 10. Escapes survive concatenation and repetition ──
assert ("\t" + "\n") * 2 == "\t\n\t\n"
