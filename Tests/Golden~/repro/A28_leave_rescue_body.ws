# Plan A.28 (found reviewing the CRASH-8 fix; fixed by T1.7, Q-14): leaving
# a rescue body by 'return', 'raise' or a runtime error skipped the ensure
# block, because the VM removed the block's handler when it entered the
# rescue body. 'break' and 'next' out of a rescue body ran it (Q-12), and so
# did falling off the end. The rescue body now runs under an ensure-only
# handler, so the ensure block runs whichever way the rescue body is left,
# once.
fun leave_by_return []
    begin
        raise "a"
    rescue e
        return "returned"
    ensure
        print "ensure after return"
    end
end
print leave_by_return []

begin
    begin
        raise "a"
    rescue e
        raise "b"
    ensure
        print "ensure after raise"
    end
rescue e
    print "outer " + e
end

begin
    begin
        raise "a"
    rescue e
        x = 1 / 0
    ensure
        print "ensure after error"
    end
rescue e
    print "outer " + e
end
# A rescue body without an ensure block passes its error on unchanged, and
# a rescue body that ends normally leaves no handler behind.
begin
    begin
        raise "a"
    rescue e
        raise "c"
    end
rescue e
    print "outer " + e
end

fun ends_normally []
    begin
        raise "a"
    rescue e
        print "rescued " + e
    ensure
        print "ensure once"
    end
    raise "later"
end
begin
    ends_normally []
rescue e
    print "outer " + e
end

# 'break' out of a rescue body runs the ensure block once and removes the
# handler.
loop i in 0..3
    begin
        raise "a"
    rescue e
        break
    ensure
        print "ensure after break"
    end
end
begin
    raise "after loop"
rescue e
    print "outer " + e
end

# A return from a rescue body nested in another begin runs both ensure blocks.
fun nested []
    begin
        begin
            raise "a"
        rescue e
            return "nested returned"
        ensure
            print "inner ensure"
        end
    ensure
        print "outer ensure"
    end
end
print nested []
# Without an ensure block the rescue body runs under no handler of its
# block, so recursing from it is not limited by the 32 handler slots.
fun retry [n]
    begin
        raise "a"
    rescue e
        if n > 0
            return retry [n - 1]
        end
        return "retried"
    end
end
print retry [60]
# 'next' and 'break' out of such a rescue body leave the enclosing
# handler in place.
begin
    loop i in 0..3
        begin
            raise "a"
        rescue e
            if i == 0
                next
            end
            break
        end
    end
    raise "still handled"
rescue e
    print "outer " + e
end
# expect: ensure after return
# expect: returned
# expect: ensure after raise
# expect: outer b
# expect: ensure after error
# expect: outer Division by zero
# expect: outer c
# expect: rescued a
# expect: ensure once
# expect: outer later
# expect: ensure after break
# expect: outer after loop
# expect: inner ensure
# expect: outer ensure
# expect: nested returned
# expect: retried
# expect: outer still handled
