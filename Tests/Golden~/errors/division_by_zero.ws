# An unhandled division by zero is a DivisionByZero error at the line of
# the division, inside the function that divides (plan T2.5).
print "before"
fun half [x]
    return x / 0   # expect-error: DivisionByZero: Division by zero
end
half [4]
print "not reached"
# expect: before
