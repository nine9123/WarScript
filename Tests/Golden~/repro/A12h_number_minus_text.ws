# Plan A.12 (DEC-8). v1: ""
a = 5         # a literal operand is a compile error (T3.8, PROP-28)
x = a - "5"   # expect-error: TypeError
