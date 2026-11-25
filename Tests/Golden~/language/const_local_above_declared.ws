# T3.8: a function above a const that assigns the const's name as a local is a
# compile error, as it is below the const (the parser rejects that one).
fun f()
    LIMIT = 2   # expect-compile-error: Cannot reassign constant 'LIMIT'
    return LIMIT
end
const LIMIT = 1
print f()
