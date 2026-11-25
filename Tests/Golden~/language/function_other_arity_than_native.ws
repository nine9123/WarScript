# T3.8 (PROP-12): a function named like a native but with no arity in common
# with it compiles, and each call goes to the one of its arity.
fun abs(a, b)
    return a + b
end
print abs(-3)      # expect: 3
print abs(1, 2)    # expect: 3
