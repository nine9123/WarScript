# isolate
# Plan A.2 (T1.2): v1 aborts the process in WarValue.Equals; v2 raises LimitExceeded.
a = {1}
a << a
b = {1}
b << b
begin
    print a == b
rescue e
    print "rescued"   # expect: rescued
end
