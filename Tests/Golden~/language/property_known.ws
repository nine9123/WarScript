# T3.8 (PROP-29): 'obj :: x' compiles when some class of the module set has
# a field or a method x, whatever obj turns out to be at runtime: here
# the instance is a Unit and only Tower declares 'range', so the VM reports
# it (NameError), and a nested class's field counts too.
class Unit [hp]
    fun heal [n]
        @hp = @hp + n
        return @hp
    end
    class Part [weight]
    end
end
class Tower [range]
end
u = new Unit [10]
print u :: hp                  # expect: 10
print u :: heal [5]            # expect: 15
print (u :: new Part [3]) :: weight   # expect: 3
begin
    print u :: range
rescue e
    print e                    # expect: 'range' is not a field of class 'Unit'
end
