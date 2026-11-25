# An error that passes through an ensure block keeps its kind and the line
# it was raised at when the block re-raises it (plan T2.5).
begin
    x = 1 / 0   # expect-error: DivisionByZero: Division by zero
ensure
    print "ensure ran"
end
# expect: ensure ran
