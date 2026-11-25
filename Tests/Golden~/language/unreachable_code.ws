# T3.8 (PROP-29): a statement after 'return', 'break', 'next' or 'raise' in
# the same block is unreachable, a warning (once per block). Code after a
# block that ends that way is not reported: the block may not run.
fun first_even(values)
    loop v in values
        if v % 2 == 0
            return v
            print "never"   # expect-warning: unreachable code after 'return'
        end
    end
    return null
end
fun count_to(n)
    total = 0
    loop i in 0..100
        if i >= n
            break
        end
        total += 1
        next
        total += 100   # expect-warning: unreachable code after 'next'
    end
    return total
end
fun fail()
    raise "stop"
    print "never"   # expect-warning: unreachable code after 'raise'
    print "never either"
end
print first_even([1, 3, 4, 5])
print count_to(3)
begin
    fail()
rescue e
    print e
end
loop true
    break
    print "never"   # expect-warning: unreachable code after 'break'
end
# expect: 4
# expect: 3
# expect: stop
