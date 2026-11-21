# How an 'ensure' block that runs while an error or a return is on its way
# out interacts with what happens inside it (T1.3, CRASH-7). The error or
# return resumes when the ensure block completes; leaving the block by
# 'raise' or 'return' replaces it; errors raised and rescued inside the block,
# or inside a function it calls, leave it alone.

# An error rescued inside the ensure block does not disturb the pending one.
begin
    begin
        raise "outer"
    ensure
        begin
            raise "inner"
        rescue e
            print "ensure rescued " + e     # expect: ensure rescued inner
        end
    end
rescue e
    print "rescued " + e                    # expect: rescued outer
end

# Nor does one raised and rescued in a function the ensure block calls.
fun quiet []
    begin
        raise "in quiet"
    rescue e
        return "quiet saw " + e
    end
end
begin
    begin
        raise "pending"
    ensure
        print quiet []                      # expect: quiet saw in quiet
    end
rescue e
    print "rescued " + e                    # expect: rescued pending
end

# Nested ensure blocks, both with an error pending: the inner raise
# replaces the error of the block it is in, and the outermost rescue sees
# the last one.
begin
    begin
        raise "a"
    ensure
        begin
            raise "b"
        ensure
            print "inner ensure"            # expect: inner ensure
        end
    end
rescue e
    print "rescued " + e                    # expect: rescued b
end

# A pending return runs every ensure block of its function, innermost first.
fun two_ensures []
    begin
        begin
            return "r"
        ensure
            print "ensure 1"                # expect: ensure 1
        end
    ensure
        print "ensure 2"                    # expect: ensure 2
    end
    return "not reached"
end
print two_ensures []                        # expect: r

# A return inside the ensure block replaces a pending return.
fun replaced []
    begin
        return "first"
    ensure
        return "second"
    end
end
print replaced []                           # expect: second

# An error raised inside the ensure block replaces a pending return.
fun raises_instead []
    begin
        return "value"
    ensure
        raise "from ensure"
    end
end
begin
    print raises_instead []
rescue e
    print "rescued " + e                    # expect: rescued from ensure
end

# An error leaving a function through its ensure block reaches the caller.
fun leaks []
    begin
        raise "leaked"
    ensure
        print "cleanup"                     # expect: cleanup
    end
end
begin
    leaks []
rescue e
    print "rescued " + e                    # expect: rescued leaked
end
print "done"                                # expect: done
