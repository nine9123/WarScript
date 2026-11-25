# T3.8: a function defined inside a function is never found by a call at
# runtime (a call looks in the definitions of its file and of the classes), so
# calling it is a compile error.
fun outer()
    fun inner()
        return 1
    end
    return inner()   # expect-compile-error: Function 'inner' is not defined
end
