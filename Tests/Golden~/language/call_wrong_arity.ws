# T3.8: a call with a number of arguments no function of that name takes is a
# compile error that lists the arities it takes (a default parameter adds one).
fun f(a, b = 2)
    return a + b
end
print f(1, 2, 3)   # expect-compile-error: Function 'f' takes 1 or 2 arguments, not 3
