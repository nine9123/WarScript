# Plan A.12 (DEC-8). v1: "True1"
a = true       # a literal operand is a compile error (T3.8, PROP-28)
x = a + 1      # expect-error: TypeError
