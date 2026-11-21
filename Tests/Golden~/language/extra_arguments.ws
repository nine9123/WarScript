# A function value called with more arguments than it has parameters
# ignores the extra ones (T3.5): its parameters, body locals and loop
# variables are the slots the compiler gave them. Before, the extra
# arguments stayed in the frame under the locals, so `i` below read the
# last extra argument and the loop never ended.
f = fun [x]
    loop i in 0..2
        print i
    end
    y = 7
    print y + x
    return x
end
print f [1, 99, 98]

# Through a global holding the function value (the Call fallback) and
# CallValue on a local.
g = f
print g [5, 6]

fun call_local [h]
    return h [10, 20, 30]
end
print call_local [f]

# A tail call of a function value with extra arguments.
fun tail [n]
    return f [n, n, n]
end
print tail [3]

# Missing arguments are still null.
k = fun [a, b]
    z = b
    if z == null
        return "missing"
    end
    return z
end
print k [1]

# expect: 0
# expect: 1
# expect: 8
# expect: 1
# expect: 0
# expect: 1
# expect: 12
# expect: 5
# expect: 0
# expect: 1
# expect: 17
# expect: 10
# expect: 0
# expect: 1
# expect: 10
# expect: 3
# expect: missing
