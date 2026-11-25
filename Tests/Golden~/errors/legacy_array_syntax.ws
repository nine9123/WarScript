# T3.12: braces are no longer arrays (nor indexes); the lexer says what to write.
x = {1, 2}   # expect-compile-error: Unexpected '{' at line 2: arrays are written [1, 2, 3] and indexed a[i]
