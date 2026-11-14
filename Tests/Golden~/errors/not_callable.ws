# Calling a local variable that holds no function is a TypeError
# (plan T2.5).
fun use [f]
    return f [1]   # expect-error: TypeError: Value is not callable
end
use [5]
