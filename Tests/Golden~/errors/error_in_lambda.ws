# An error in a lambda is raised at its line (plan T2.5).
f = fun [x]
    return x / 0   # expect-error: DivisionByZero
end
fun apply [g]
    return g [1]
end
apply [f]
