# Plan A.12 (DEC-8). v1: 0
a = "abc"    # a literal operand is a compile error (T3.8, PROP-28)
x = -a       # expect-error: TypeError
