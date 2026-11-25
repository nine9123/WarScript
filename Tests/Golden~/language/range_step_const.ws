# T3.8: a range step that reads a 'const' declared as a number literal is
# checked against its value once the module set is compiled
# (range_step_not_positive.ws is the literal case).
const STEP = 0
loop i in 0..3 by STEP   # expect-compile-error: The step of a range loop must be greater than 0, not 0
    print i
end
