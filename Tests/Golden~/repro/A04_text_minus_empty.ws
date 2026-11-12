# Plan A.4 (T1.4): v1 let an ArgumentException escape Run(). Since Phase 1 this
# prints "abc"; Phase 3 (T3.6) removes the operator and expects TypeError instead.
x = "abc" - ""
print x           # expect: abc
