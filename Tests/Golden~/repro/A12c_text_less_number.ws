# Plan A.12 (DEC-8). v1: True
a = "10"       # a literal operand is a compile error (T3.8, PROP-28)
x = a < 9      # expect-error: TypeError
