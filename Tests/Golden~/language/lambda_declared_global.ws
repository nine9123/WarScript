# T3.4 (PROP-10, PROP-16): a lambda that declares a name global reads the
# global, even where the code around it has a local of that name.
count = 10
fun run []
    count = 1                   # a local of run
    bump = fun []
        global count
        count = count + 5
        return count
    end
    return bump []
end
print run []
print count
# expect: 15
# expect: 15
