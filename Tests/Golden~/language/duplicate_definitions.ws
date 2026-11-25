# T3.7, PROP-12, plan 5.8: a second definition of a function with an arity
# the first one already takes (default parameters included), or of a class
# name, is a compile error at the second; in v1 the last definition won.
# Same name with other arities, a class named like a function and methods
# of different classes are fine. Cross-file cases: ImportTests.
fun f(a, b = 1)
    return a + b
end
fun f() return 0 end
class Unit end
class f end
fun f(a)   # expect-compile-error: Function 'f' with 1 argument is already defined at line 6
    return a
end
