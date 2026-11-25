# T3.12: a legacy parameter list in brackets is reported as such.
fun f [a]   # expect-compile-error: Parameters are written in parentheses: f(...)
    return a
end
