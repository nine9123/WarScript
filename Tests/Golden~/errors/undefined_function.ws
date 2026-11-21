# Calling a function that does not exist is a NameError (plan T2.5). The
# compiler reports a call no function or global can answer (T3.8), so the
# name is written as a global on a path that never runs.
fun caller []
    missing [1]   # expect-error: NameError: Function 'missing' with 1 args is not defined
end
caller []
if false
    missing = 0
end
