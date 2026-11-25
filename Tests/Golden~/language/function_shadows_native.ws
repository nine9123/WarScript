# T3.8 (PROP-12, DEC-21): a function with the name and an arity of a native
# is a compile error (v1 logged "Shadowing native function" and the script's
# function won). 'abs' is MathLibrary's, with one parameter.
fun abs(n)   # expect-compile-error: Function 'abs' with 1 argument shadows a native function
    return n
end
print abs(-1)
