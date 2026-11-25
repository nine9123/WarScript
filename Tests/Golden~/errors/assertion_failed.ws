# A failed assert is an AssertionFailed error at its line (plan T2.5).
x = 1
assert x == 2   # expect-error: AssertionFailed: Assertion error at line 3
