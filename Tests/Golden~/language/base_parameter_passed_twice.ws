# T3.8 (PROP-29) checks the names a class declares, not the arguments it
# passes on: a base type may be given the same parameter twice.
class Pair [a, b]
    fun sum []
        return @a + @b
    end
end
class Twin [v] : Pair [v, v]
end
print (new Twin [4]) :: sum []
# expect: 8
