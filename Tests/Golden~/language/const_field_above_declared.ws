# T3.8: a class body above a const that assigns the const's name as a field is
# a compile error, as it is below the const (the parser rejects that one).
class Box(a)
    SIZE = 2   # expect-compile-error: Cannot reassign constant 'SIZE'
end
const SIZE = 1
b = new Box(1)
print SIZE
