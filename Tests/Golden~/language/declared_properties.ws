# T3.6 (PROP-15, plan 5.5): an instance's properties are the fields its class
# parts declare (constructor parameters and class-body assignments, 5.7).
# Anything else is a NameError, read or written, from any code.
class Base [a]
    b = 2
    if false
        late = 0
    end
end
class Derived [a, c] : Base [a]
    d = 4
    fun set_late [v]
        @late = v
    end
end
x = new Derived [1, 3]
print x :: a + x :: b + x :: c + x :: d
# Declared, never assigned: null until written.
print x :: late == null
x :: late = 5
print x :: late
x :: b = 20
print x :: b
x :: set_late [6]
print x :: late
# A cast is the same instance with the same fields (one field scope, Q-16).
print (x as Base) :: d
fun try_read [o, name]
    begin
        if name == "zz"
            return o :: zz
        end
        return o :: yy
    rescue e
        return e
    end
end
print try_read [x, "zz"]
print try_read [x as Base, "yy"]
begin
    x :: zz = 1
rescue e
    print e
end
x :: b = {}
begin
    x :: arr{0} = 1
rescue e
    print e
end
# expect: 10
# expect: true
# expect: 5
# expect: 20
# expect: 6
# expect: 4
# expect: 'zz' is not a field of class 'Derived'
# expect: 'yy' is not a field of class 'Base'
# expect: 'zz' is not a field of class 'Derived'
# expect: 'arr' is not a field of class 'Derived'
