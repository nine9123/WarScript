# known-failing: CRASH-3
# Plan A.4 (T1.4): v1 lets an ArgumentException escape Run(). After Phase 1 this
# prints "abc"; Phase 3 (T3.6) removes the operator and expects TypeError instead.
x = "abc" - ""
print x           # expect: abc
