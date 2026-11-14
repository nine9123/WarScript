# Constructors run as frames on the current VM (T1.1). 'new' first builds
# the whole instance (base-class parts and shared properties included), then
# runs the constructor bodies: base classes depth-first in declaration order,
# then the derived class. The expression's value is the instance; whatever a
# constructor body returns is discarded.

class A [x]
    print "A " + x
end
class B [y]
    print "B " + y
end
class C [x, y] : A [x], B [y]
    print "C " + x + " " + y
end
c = new C [1, 2]            # expect: A 1
                            # expect: B 2
                            # expect: C 1 2
print c :: x + c :: y       # expect: 3

# Depth-first: a base's own bases run before the next base.
class Root
    print "Root"
end
class Left : Root
    print "Left"
end
class Right
    print "Right"
end
class Bottom : Left, Right
    print "Bottom"
end
b = new Bottom              # expect: Root
                            # expect: Left
                            # expect: Right
                            # expect: Bottom

# A shared property written by a base constructor is seen by the derived one.
class Counter [n]
    n = n + 10
end
class Tally [n] : Counter [n]
    print "tally " + n
end
t = new Tally [1]           # expect: tally 11
print t :: n                # expect: 11

# 'return' ends a constructor body; its value is discarded.
class Early [v]
    if v > 0
        return 99
    end
    print "not returned"
end
e = new Early [1]
print e :: v                # expect: 1
e = new Early [0]           # expect: not returned

# Constructors can create instances, call functions and call methods.
class Leaf2 [v]
    fun twice []
        return @v * 2
    end
end
fun helper [v]
    return v + 100
end
class Tree [v]
    left = new Leaf2 [v]
    w = left :: twice []
    h = helper [v]
end
tr = new Tree [5]
print tr :: w + " " + tr :: h   # expect: 10 105

# An error in a base constructor unwinds to the code that executed 'new':
# the derived constructor does not run, and 'begin/rescue' catches it.
class Fragile [v]
    if v < 0
        raise "negative"
    end
end
class Sturdy [v] : Fragile [v]
    print "sturdy " + v
end
begin
    s = new Sturdy [-3]
    print "not reached"
rescue err
    print "rescued " + err      # expect: rescued negative
end
s = new Sturdy [3]              # expect: sturdy 3

# An error rescued inside a constructor stays inside it.
class Careful [v]
    begin
        q = v / 0
    rescue err
        print "careful " + err  # expect: careful Division by zero
    end
end
ca = new Careful [1]
print "after careful"           # expect: after careful

# Recursion in a constructor ends with a rescuable "Stack overflow".
class Deep [v]
    if v < 1000
        next_one = new Deep [v + 1]
    end
end
begin
    d = new Deep [0]
rescue err
    print "deep " + err          # expect: deep Stack overflow
end

# Indirect inheritance cycles are a rescuable error at 'new' (CRASH-5, Q-11).
class Ping : Pong
end
class Pong : Ping
end
begin
    p = new Ping
rescue err
    print err                    # expect: Class 'Ping' inherits from itself
end
