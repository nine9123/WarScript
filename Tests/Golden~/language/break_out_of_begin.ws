# Q-12 (CRASH-8): 'break' and 'next' leave the begin blocks between them and
# their loop like 'return' does: each block's handler is removed, its ensure
# block runs (unless the jump is inside it), and what an ensure block was
# going to re-raise or return is discarded when the jump leaves it.

# From begin bodies, nested, inside a function (stack locals).
fun collect []
    out = {}
    loop i in 0..5
        begin
            begin
                if i == 1
                    next
                end
                if i == 3
                    break
                end
                out << i
            ensure
                out << "e" + i
            end
        rescue err
            out << "bad"
        ensure
            out << "E" + i
        end
    end
    return out
end
print collect []
# expect: [0, e0, E0, e1, E1, 2, e2, E2, e3, E3]

# From a rescue body: the ensure block still runs.
loop true
    begin
        raise "x"
    rescue e
        break
    ensure
        print "ensure after rescue"      # expect: ensure after rescue
    end
end

# From an ensure block entered with an error to re-raise: the error is dropped.
n = 0
loop n < 3
    n += 1
    begin
        begin
            raise "pending"
        ensure
            next
        end
    rescue e
        print "not reached: " + e
    end
end
print "n=" + n                           # expect: n=3

# From an ensure block entered by 'return': the return is dropped.
fun leave_return []
    loop true
        begin
            return 1
        ensure
            break
        end
    end
    return 2
end
print leave_return []                    # expect: 2

# A begin block outside the loop keeps its handler.
begin
    loop i in 0..3
        begin
            break
        rescue e
            print "stale rescue"
        end
    end
    raise "outer"
rescue e
    print "rescued " + e                 # expect: rescued outer
end

# Two breaks in one loop pop the right number of block scopes, so a later
# assignment is a global (it used to land in a leaked 'if' scope).
fun read_y []
    return y
end
loop i in 0..10
    if i == 50
        break
    end
    if i == 2
        break
    end
end
y = 5
print read_y []                          # expect: 5
