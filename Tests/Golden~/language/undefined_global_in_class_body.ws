# T3.8 (PROP-27): a class body finds a field first, then a global; a name
# that is neither is a compile error.
class Unit [hp]
    max = hp
    armor = base_armor   # expect-compile-error: undefined variable 'base_armor'
end
