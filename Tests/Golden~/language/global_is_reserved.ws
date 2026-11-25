# T3.3 (PROP-10): `global` is a keyword, so it cannot be a name.
fun f()
    global = 1   # expect-compile-error: 'global' is a reserved word
end
