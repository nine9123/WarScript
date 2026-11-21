# T3.8 (PROP-12): a default parameter makes a function callable with fewer
# arguments, so 'sqrt [n, base = 2]' shadows MathLibrary's 'sqrt [n]' too.
fun sqrt [n, base = 2]   # expect-compile-error: Function 'sqrt' with 1 argument shadows a native function
    return n
end
