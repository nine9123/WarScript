# T3.6: text is immutable, so an index write raises TypeError (DEC-10, plan 5.5).
s = "abc"
s[0] = "x"   # expect-error: TypeError: Cannot assign to an index of text: text is immutable
