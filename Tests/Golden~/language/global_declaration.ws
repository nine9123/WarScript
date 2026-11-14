# T3.3/T3.4 (PROP-10): `global a, b` is a statement of a function, method,
# lambda or class body, nested blocks included; assignments to those names
# write the instance globals (in a class body too, instead of declaring a
# field).
count = 0
total = 0

fun bump [n]
    global count, total
    count = count + 1
    total = total + n
end

fun bump_twice [n]
    if n > 0
        global count,
            total
        count = count + 2
    end
end

fun one_line [n] global count count = count + n end

class Counter [start]
    global count
    fun read []
        global total
        return @start
    end
end

add = fun [n]
    global count
    count = count + n
end

bump [5]
bump [7]
bump_twice [1]
one_line [10]
add [100]
print count
print total
print (new Counter [3]) :: read []
# expect: 114
# expect: 12
# expect: 3
