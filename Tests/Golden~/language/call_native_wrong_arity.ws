# T3.8: natives are known when the script compiles, so a wrong number of
# arguments to one is a compile error too.
x = abs(1, 2)   # expect-compile-error: Function 'abs' takes 1 argument, not 2
