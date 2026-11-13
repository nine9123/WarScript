# known-failing: SEM-13
# Plan A.28 (found reviewing the CRASH-8 fix): leaving a rescue body by
# 'return', 'raise' or a runtime error skips the ensure block, because the
# VM removes the block's handler when it enters the rescue body. 'break' and
# 'next' out of a rescue body run it since the CRASH-8 fix (Q-12), and so
# does falling off the end. Q-14 default: the ensure block runs whichever
# way the rescue body is left.
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
# expect: ensure after return
# expect: returned
# expect: ensure after raise
# expect: outer b
# expect: ensure after error
# expect: outer Division by zero
