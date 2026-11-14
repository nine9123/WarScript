# known-failing: T3.4-fields
# Plan A.18 (DEC-6): `@x` is checked at compile time against the fields of
# the class and its bases. Needs the class field list (T3.4, second half).
class Unit [hp]
    fun hit [d]
        return @nope - d   # expect-compile-error: nope
    end
end
