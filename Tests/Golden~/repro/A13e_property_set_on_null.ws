# known-failing: SEM-7
# Plan A.13 (DEC-10). v1 ignores the write.
x = null
x :: hp = 5       # expect-error: TypeError
