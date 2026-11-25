# T3.12 review: `wait` and `until` are names, but a '(' after them (right after
# `yield`) groups the duration or the condition; it is not a call.
ready = false
fun waiter()
    yield wait (1 + 1) * 0.5
    yield until (ready == true)
    print "done"
end
id = start_coroutine("waiter", [])
print "started"
# expect: started
