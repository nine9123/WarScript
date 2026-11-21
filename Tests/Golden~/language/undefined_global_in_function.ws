# T3.8 (PROP-27): a function reads globals freely, but only names something
# writes; the function never has to run for the error.
fun total [n]
    return n + bonus   # expect-compile-error: undefined variable 'bonus'
end
