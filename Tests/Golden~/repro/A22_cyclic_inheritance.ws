# isolate
# Plan A.22 (found by the fuzzer, T0.7): a class that inherits from itself,
# here through a redefinition, makes v1's 'new' recurse in
# InstantiateClassDefShared until the process aborted with a stack overflow.
# Since T1.1 the allocation step detects the cycle and raises a rescuable
# error at 'new' (the Q-11 default). Still isolated, so that a regression
# cannot take the test host down.
class A
end
class A : A
end
begin
    a = new A
rescue e
    print "rescued " + e   # expect: rescued Class 'A' inherits from itself
end
