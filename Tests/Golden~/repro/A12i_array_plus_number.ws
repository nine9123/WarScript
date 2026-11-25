# Plan A.12 (DEC-8). v1: [1, 2, 5] (PROP-6)
a = [1, 2]       # a literal operand is a compile error (T3.8, PROP-28)
x = a + 5        # expect-error: TypeError
