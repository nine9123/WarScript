# ──────────────────────────────────────────────────────
# test_regression_textvalue_setvalue.ws
# v1 bug: TextValue.SetValue(index, value) inserted at the index instead
# of replacing the character. Text is immutable since T3.6 (DEC-10), so
# the write raises TypeError and the text keeps its value.
# ──────────────────────────────────────────────────────

text = "abcde"
begin
    text{2} = "X"
rescue e
    print e
end
assert text == "abcde"
text{0} = "X"     # expect-error: TypeError: text is immutable
# expect: Cannot assign to an index of text: text is immutable
