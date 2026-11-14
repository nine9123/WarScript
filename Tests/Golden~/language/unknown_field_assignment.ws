# T3.4 (5.7, DEC-6, PROP-15): `@x = ...` in a method must name a field of
# the class (constructor parameters and class-body assignments) or of its
# bases; a method cannot add one.
class Unit [hp]
    armor = 1
    fun buff []
        @armor = @armor + 1
        @shield = 5   # expect-compile-error: 'shield' is not a field of class 'Unit'
    end
end
