# T3.12: `f [1]` (the legacy call) indexes a variable `f` now; when no
# variable of that name exists but a function does, the error says so.
fun f(a)
    return a
end
x = f [1]   # expect-compile-error: undefined variable 'f' ('f' is a function: call it as f(...))
