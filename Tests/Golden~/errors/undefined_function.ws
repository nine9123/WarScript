# Calling a function that does not exist is a NameError (plan T2.5).
fun caller []
    missing [1]   # expect-error: NameError: Function 'missing' with 1 args is not defined
end
caller []
