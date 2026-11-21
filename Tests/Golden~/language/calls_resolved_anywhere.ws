# T3.8: a call compiles when a function of that name takes that many arguments
# anywhere a call can find it (later in the file, a method of a class: a
# function called from a method finds the method's class), or when the name is
# a global that may hold a function value (written anywhere, even on a path
# that does not run: NameError at runtime), or a field in a class body.
fun first []
    return second [] + 1
end
fun second []
    return 1
end
print first []                 # expect: 2
fun via_method []
    return helper []
end
class Unit []
    fun helper []
        return 10
    end
    fun go []
        return via_method []
    end
end
print (new Unit []) :: go []   # expect: 10
double = fun [x] return x * 2 end
print double [4]               # expect: 8
class Holder [op]
    result = op [3]
end
print (new Holder [double]) :: result   # expect: 6
if false
    never = 0
end
begin
    never [1]
rescue e
    print e                    # expect: Function 'never' with 1 args is not defined
end
