# T3.3 (DEC-6): `@x` is lexed as `this :: x`, in methods, class bodies and
# interpolations.
class Unit [hp]
    max = @hp * 2
    fun hit [d]
        @hp = @hp - d
        @hp -= 1
        return "hp {@hp}/{@max}"
    end
    fun heal []
        @hp = @max
        return @describe []
    end
    fun describe []
        return "full: " + @hp
    end
end

u = new Unit [10]
print u :: hit [3]
print u :: heal []
print u :: hp
# expect: hp 6/20
# expect: full: 20
# expect: 20
