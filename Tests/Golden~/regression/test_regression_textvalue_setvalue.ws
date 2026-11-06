# ──────────────────────────────────────────────────────
# test_regression_textvalue_setvalue.ws
# Bug: TextValue.SetValue(index, value) inserts at index
#      instead of replacing the character at that index.
#
# "hello"{2} = "X" should produce "heXlo", not "heXllo"
# ──────────────────────────────────────────────────────

# ── Direct array-index assignment on a variable ──
text = "abcde"
text{0} = "X"
assert text == "Xbcde"

text2 = "abcde"
text2{2} = "Z"
assert text2 == "abZde"

text3 = "abcde"
text3{4} = "Y"
assert text3 == "abcdY"

# ── Replacing should not change string length ──
s = "hello"
s{1} = "a"
assert s == "hallo"

# ── Replace at index 0 ──
s2 = "world"
s2{0} = "W"
assert s2 == "World"
