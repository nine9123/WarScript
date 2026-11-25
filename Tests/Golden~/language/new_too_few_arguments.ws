# T3.8 (PROP-29): too few constructor arguments are a compile error too;
# pass null for a parameter left empty.
fun make()
    return new Unit("red")   # expect-compile-error: Class 'Unit' takes 2 arguments, not 1
end
class Unit(team, hp)
end
