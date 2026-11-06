# known-failing: SEM-7
# Plan A.13 (DEC-10). v1 reads null.
arr = {1, 2, 3}
x = arr{"a"}      # expect-error: TypeError
