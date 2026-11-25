# Q-15, SEM-14: a yield outside a coroutine in a function the script body calls
# is a TypeError at the yield (it used to end Run() silently, the rest of the
# script not run). A top-level yield still ends the run (v1); a coroutine may
# yield in any function it calls.
fun step()
    yield wait 1   # expect-error: TypeError: Cannot yield outside a coroutine
    return 1
end
print "before"
print step()
print "not reached"
# expect: before
