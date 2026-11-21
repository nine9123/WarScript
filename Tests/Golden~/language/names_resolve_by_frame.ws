# T3.5: where a bare name that is not a local resolves depends on the frame,
# not on scopes pushed by calls: a class body finds a field first, then a
# global; a function, lambda or method finds a global, even when called from
# a class body or a method of an instance with a field of that name.
hp = "global hp"
pick = fun [] return "global pick" end

fun read_hp []
    return hp
end

fun read_hp_twice []
    return read_hp []
end

class Unit [hp]
    pick = fun [] return "field pick" end
    from_body = hp
    from_function = read_hp []
    from_tail_call = read_hp_twice []
    called = pick []
    fun from_method []
        return "{hp} / {@hp} / {read_hp []}"
    end
end

u = new Unit [7]
print u :: from_body
print u :: from_function
print u :: from_tail_call
print u :: called
print u :: from_method []
print pick []

fun bump []
    global hp
    hp = hp + "!"
end
class Bumper [n]
    bump []
end
b = new Bumper [1]
print hp
# expect: 7
# expect: global hp
# expect: global hp
# expect: field pick
# expect: global hp / 7 / global hp
# expect: global pick
# expect: global hp!
