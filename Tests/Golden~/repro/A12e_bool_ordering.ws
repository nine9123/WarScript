# Plan A.12 (DEC-8). v1: False (PROP-17)
a = true           # a literal operand is a compile error (T3.8, PROP-28)
x = a < false      # expect-error: TypeError
