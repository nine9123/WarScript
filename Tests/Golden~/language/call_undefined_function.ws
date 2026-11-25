# T3.8: a call by name that no function and no global can answer is a compile
# error; the function holding it never has to run.
fun caller()
    return helper(1)   # expect-compile-error: Function 'helper' is not defined
end
