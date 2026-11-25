# Q-15, SEM-14: the error for a yield outside a coroutine is rescuable, from a
# function, a method and a lambda; a coroutine still yields in a called function.
fun step()
    yield
    return 1
end
class Unit()
    fun wait_turn()
        yield
    end
end
lam = fun() yield end
begin
    step()
rescue err
    print "function: " + err
end
begin
    new Unit().wait_turn()
rescue err
    print "method: " + err
end
begin
    lam()
rescue err
    print "lambda: " + err
end
fun runner()
    print "co " + step()
end
start_coroutine("runner", [])
print "end"
# expect: function: Cannot yield outside a coroutine
# expect: method: Cannot yield outside a coroutine
# expect: lambda: Cannot yield outside a coroutine
# expect: end
