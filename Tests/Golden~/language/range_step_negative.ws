# T3.8: a negative literal step does not count down (range_step_not_positive.ws).
loop i in 3..0 by -1   # expect-compile-error: must be greater than 0, not -1
    print i
end
