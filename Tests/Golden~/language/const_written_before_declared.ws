# T3.8: a constant is protected from writes the parser reads before its
# declaration too: the compiler checks every global write of the module set.
LIMIT = 1   # expect-compile-error: Cannot reassign constant 'LIMIT'
const LIMIT = 2
print LIMIT
