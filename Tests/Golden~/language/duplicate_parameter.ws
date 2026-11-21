# T3.8 (PROP-29): a parameter list names each parameter once.
fun add [a, b, a]   # expect-compile-error: Parameter 'a' is declared twice
    return a + b
end
print add [1, 2, 3]
