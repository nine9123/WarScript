# known-failing: CRASH-6
# Plan A.23 (found by the fuzzer, T0.7): v1's compiler throws an
# InvalidCastException (ConstantExpression to VariableExpression) that
# escapes to the host; v2 reports a compile error.
class P [n]
end
p = new P [1]
x = p as "R" :: n   # expect-compile-error:
