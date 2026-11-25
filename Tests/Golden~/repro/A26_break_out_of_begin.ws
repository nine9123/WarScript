# budget: 10000
# Plan A.26 (found by the fuzzer during T1.2): 'break' (or 'next') inside a
# begin block leaves the block's exception handler on the VM. A later error
# outside the loop is caught by that stale handler: v1 runs the rescue body
# again with the wrong scope depth and here ends in a host fault
# ("Stack empty" from MemoryContext.EndScope); without the 'if' it loops in
# the rescue body until the budget runs out. v2 removes the handler when the
# loop is left, so the assertion is an unhandled error and nothing is printed.
loop true
    if true
        begin
            break
        rescue e
            print "stale rescue"
        end
    end
end
assert false
# expect-error: AssertionFailed
