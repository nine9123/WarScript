# An ensure block that compiles to no code (empty, or only `global`) used to
# make the compiler emit bytecode its own verifier rejects: a host fault at
# compile time (fuzzer signature 9c774f567b1277af, found during T3.12).
begin
    print "body"
ensure
end

fun returns()
    begin
        return 1
    ensure
    end
    return 2
end
print returns()

fun declares()
    begin
        x = 5
    ensure
        global unused_here
    end
    return x
end
print declares()

begin
    begin
        raise "inner"
    ensure
    end
rescue e
    print "outer rescued " + e
end

begin
    begin
        raise "first"
    rescue e
        raise "from rescue: " + e
    ensure
    end
rescue e
    print e
end

loop i in 0..3
    begin
        if i == 1
            next
        end
        print "i=" + i
    ensure
    end
end

# expect: body
# expect: 1
# expect: 5
# expect: outer rescued inner
# expect: from rescue: first
# expect: i=0
# expect: i=2
