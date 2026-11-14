# Plan A.18 (DEC-6): the 8.4 example in its v2 form. A bare name in a
# method is a local; fields are `@x`.
class Unit [hp]
    armor = 2               # class body: declares a field
    fun hit [d]
        dealt = d - @armor  # local + field
        @hp = @hp - dealt
        return @hp
    end
end
u = new Unit [20]
print u :: hit [5]
print u :: hit [5]
print u :: hp
# expect: 17
# expect: 14
# expect: 14
