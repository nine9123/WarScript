# known-failing: SEM-7
# Plan A.13 (DEC-10): text is immutable in v2. v1 changes s to "xbc".
s = "abc"
s{0} = "x"        # expect-error: TypeError
