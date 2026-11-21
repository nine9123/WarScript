# Plan A.10 (DEC-5): v2 as written. v1 prints 3 for the global too.
count = 10
fun helper []
    count = 0
    loop i in 0..3
        count += 1
    end
    return count
end
print helper []   # expect: 3
print count       # expect: 10
