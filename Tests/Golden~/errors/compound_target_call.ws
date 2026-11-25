# T3.13, PROP-32: `target op= e` evaluates its target twice (once to read,
# once to write), so a call in the target would run twice. It is a compile
# error; the fix is to put the call's result in a variable first.
fun g()
    return [1]
end
g()[0] += 1   # expect-compile-error: The target of '+=' is evaluated twice and calls 'g'
