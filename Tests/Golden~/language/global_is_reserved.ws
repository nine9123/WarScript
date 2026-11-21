# T3.3 (PROP-10): `global` is a keyword; v1 scripts that used it as a name
# are renamed `global_` by the migration tool.
fun f []
    global = 1   # expect-compile-error: 'global' is a reserved word
end
