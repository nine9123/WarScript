# T3.12 review: only a name can be called. `f(1)(2)` (and `arr[0](2)`) used to
# drop the left operand without a word; it is a syntax error.
fun mk(n)
    return fun(x) return x * 10 end
end
print mk(1)(2)   # expect-compile-error: Unexpected `(` at line 6: expected an operator or end of expression (only a name can be called
