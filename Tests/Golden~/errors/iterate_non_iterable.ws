# Looping over a value that is not an array is a TypeError (plan T2.5).
n = 5
loop v in n   # expect-error: TypeError: Unable to iterate
    print v
end
