# isolate
# known-failing: CRASH-5
# Plan A.22 (found by the fuzzer, T0.7): a class that inherits from itself,
# here through a redefinition, makes v1's 'new' recurse in
# InstantiateClassDefShared until the process aborts with a stack overflow.
# v2 must not crash; until the owner decides between a compile error and a
# runtime error (Q-11), this expects a rescuable error at 'new'.
class A
end
class A : A
end
begin
    a = new A
rescue e
    print "rescued"   # expect: rescued
end
