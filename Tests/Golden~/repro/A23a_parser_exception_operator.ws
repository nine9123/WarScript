# Plan A.23 (found by the fuzzer, T0.7): v1's expression reader throws a plain
# System.Exception ("Operator LeftParen is not a binary operator") that
# escapes to the host; v2 reports a compile error.
x = (1]   # expect-compile-error:
