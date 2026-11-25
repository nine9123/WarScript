# T3.8: a parameter with a default value named like a constant: the default's
# desugared assignment writes the parameter, not the constant, so it compiles.
const SPEED = 1
fun move(SPEED = 2)
    return SPEED
end
print move()
print move(5)
print SPEED
# expect: 2
# expect: 5
# expect: 1
