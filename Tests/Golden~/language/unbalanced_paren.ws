# T1.8 (CRASH-6): a '(' still open at the end of an expression is a compile
# error (A.23a covers `(1]`).
x = (1 + 2   # expect-compile-error: Unbalanced '('
