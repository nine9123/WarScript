# ──────────────────────────────────────────────────────
# test_gapfill_string_mutation.ws
# Gap: string index WRITE was completely untested.
# All existing tests only READ string indices.
# ──────────────────────────────────────────────────────

# ── Basic single-character replacement ──
s = "abcde"
s{0} = "X"
assert s == "Xbcde"

s{4} = "Y"
assert s == "XbcdY"

s{2} = "Z"
assert s == "XbZdY"

# ── Replace should NOT change length ──
t = "hello"
t{1} = "a"
assert t == "hallo"
t{0} = "H"
assert t == "Hallo"
t{4} = "!"
assert t == "Hall!"

# ── Replace all characters one by one ──
r = "abc"
r{0} = "x"
r{1} = "y"
r{2} = "z"
assert r == "xyz"

# ── Replace with digit characters ──
d = "---"
d{0} = "1"
d{1} = "2"
d{2} = "3"
assert d == "123"

# ── Replace same index multiple times ──
m = "aaa"
m{1} = "b"
assert m == "aba"
m{1} = "c"
assert m == "aca"
m{1} = "d"
assert m == "ada"

# ── Class property string mutation ──
class Label[text]
end
lbl = new Label["hello"]
lbl :: text{0} = "H"
assert lbl :: text == "Hello"

# ── String in array, mutate via temporary variable ──
arr = {"abc", "def"}
temp = arr{0}
temp{1} = "X"
arr{0} = temp
assert arr{0} == "aXc"

# ── String mutation inside a function ──
fun replace_first[s, c]
    s{0} = c
    return s
end
assert replace_first["world", "W"] == "World"

# ── String mutation inside a loop ──
word = "0000"
loop i in 0..4
    if i == 0
        word{i} = "a"
    elif i == 1
        word{i} = "b"
    elif i == 2
        word{i} = "c"
    else
        word{i} = "d"
    end
end
assert word == "abcd"
