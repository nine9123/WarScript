# T3.4 (DEC-5, PROP-9, PROP-10, PROP-20): in a function, method or lambda a
# name it assigns is a local of the whole body; a global is read freely and
# written only when declared `global`.
count = 10
label = "global"

fun helper []
    count = 0              # a local: the global stays 10
    loop i in 0..3
        count += 1
    end
    return count
end
print helper []
print count

fun reads_global []
    return label + "!"     # reading a global needs no declaration
end
print reads_global []

fun branches [flag]
    if flag
        made = "inside"
    end
    return made            # function scope: null when the branch did not run
end
print branches [true]
print branches [false]

fun bump [n]
    global count, created
    count = count + n
    created = "made by bump"   # a declared global that did not exist
end
bump [5]
print count
print created

fun fib [n]
    if n < 2
        return n
    end
    a = fib [n - 1]        # each call has its own slots
    b = fib [n - 2]
    return a + b
end
print fib [15]

triple = fun [x]
    t = x * 3              # a lambda's own local
    return t
end
print triple [4]

class Unit [hp]
    fun hit [d]
        left = @hp - d     # a local of the method
        @hp = left
        return label + " " + left    # globals are visible in methods (PROP-20)
    end
end
u = new Unit [10]
print u :: hit [3]
print u :: hp

fun first_even [items]
    loop item in items
        begin
            if item % 2 == 0
                break
            end
            last_odd = item
        ensure
            seen = last_odd    # compiled again where `break` leaves the block
        end
    end
    return seen
end
print first_even [{1, 3, 4, 5}]
# expect: 3
# expect: 10
# expect: global!
# expect: inside
# expect: null
# expect: 15
# expect: made by bump
# expect: 610
# expect: 12
# expect: global 7
# expect: 7
# expect: 3
