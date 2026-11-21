# T3.8: a range loop runs while its variable is less than the upper bound, so
# a step that is not greater than 0 either never runs the body (a descending
# range) or never ends the loop. A literal one is a compile error, at the step.
# A step from a variable or a call is not checked (the instruction budget
# stops a loop that never ends).
loop i in 0..3 by 0   # expect-compile-error: The step of a range loop must be greater than 0, not 0
    print i
end
