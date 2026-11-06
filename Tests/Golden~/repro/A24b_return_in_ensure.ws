# known-failing: CRASH-7
# Plan A.24 (found by the fuzzer, T0.7): 'return' inside 'ensure' while an
# error is propagating. v1 returns 1 but leaves the VM with a pending
# exception and a handler behind, so the error comes back later: here the
# outer 'rescue' does not catch it, "after" is not printed, and the run ends
# with "first" unhandled. The 'return' discards the error, as it already does
# in v1 when nothing rescues around the call.
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
