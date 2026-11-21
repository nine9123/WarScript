# T3.8 (PROP-28): only a read of the constant itself is typed. A parameter or
# a field named like a constant, or the value read through a variable or a
# call, compiles and raises the runtime TypeError (static_operand_const.ws is
# the static case).
const LIMIT = 10
enum Rank
    LOW
    HIGH
end

fun attempt [f]
    begin
        print f []
    rescue e
        print e
    end
end

fun minus_text [LIMIT]
    return LIMIT - "x"
end

class Box [LIMIT]
    fun minus_text []
        return @LIMIT - "x"
    end
end

fun same [v]
    return v
end

attempt [fun [] return minus_text ["a"] end]             # expect: Cannot apply '-' to text and text
attempt [fun [] return (new Box ["b"]) :: minus_text [] end]   # expect: Cannot apply '-' to text and text
attempt [fun [] return same [LIMIT] - "x" end]           # expect: Cannot apply '-' to number and text
print LIMIT + Rank :: HIGH + Rank :: count               # expect: 13
print Rank :: name [1] + "!"                             # expect: HIGH!
print Rank :: values + {2}                               # expect: [0, 1, 2]
