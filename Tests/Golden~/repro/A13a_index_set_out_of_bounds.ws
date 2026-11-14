# known-failing: SEM-7
# Plan A.13 (DEC-10). v1 ignores the write.
arr = {1, 2, 3}
arr{10} = 99      # expect-error: IndexError
