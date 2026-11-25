# T3.8: a range loop runs while its variable is less than the upper bound, so
# a step that is not greater than 0 never runs the body or never ends the
# loop. A literal one is a compile error (language/range_step_not_positive.ws);
# one known only at runtime is an ArgumentError when the loop starts, at the
# step, before the first iteration and even when the range is empty.
fun down()
    return -1
end
step = 2
loop i in 0..5 by step
    print i
end
begin
    loop i in 3..0 by down()
        print i
    end
rescue e
    print e
end
step = 0
loop i in 0..3 by step   # expect-error: ArgumentError: The step of a range loop must be greater than 0, not 0
    print i
end
# expect: 0
# expect: 2
# expect: 4
# expect: The step of a range loop must be greater than 0, not -1: the loop runs while its variable is less than the upper bound
