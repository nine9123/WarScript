# isolate
# Plan A.1 (T1.2): v1 aborts the process with a stack overflow in WarValue.ToString().
a = {1}
a << a
print a           # expect: [1, [...]]
