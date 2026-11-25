# T3.12: the legacy call with several arguments, `f [a, b]`, reads as an
# index followed by a ','; the error says how a call is written now rather
# than that a ']' was expected.
fun f(a, b)
    return a + b
end
x = f [1, 2]   # expect-compile-error: Unexpected ',' in an index at line 7: an index takes one value (calls are written f(a, b))
