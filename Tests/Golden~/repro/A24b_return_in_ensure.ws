# Plan A.24 (CRASH-7, found by the fuzzer, T0.7; fixed by T1.3): 'return'
# inside 'ensure' while an error is propagating. v1 returned 1 but left the
# VM with a pending exception and a handler behind, so the error came back
# later: the outer 'rescue' did not catch it, "after" was not printed, and
# the run ended with "first" unhandled. The 'return' discards the error.
fun f []
    begin
        raise "first"
    ensure
        return 1
    end
end
begin
    print f []              # expect: 1
rescue e
    print "rescued " + e
end
print "after"               # expect: after
