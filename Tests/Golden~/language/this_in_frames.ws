# T3.5: 'this' is part of the call frame. A method or constructor frame has
# its instance; a method called by its bare name from another method, and a
# function or lambda called from one, see the caller's; code outside any
# class has none.
class Counter [n]
    fun twice []
        return @n * 2
    end
    fun report []
        return "{@n} twice is {twice []}"
    end
    fun other [c]
        # c's method runs with c as 'this', then this method's 'this' is back
        return "{c :: report []} / {@n}"
    end
    label = report []
end

a = new Counter [3]
b = new Counter [5]
print a :: label
print a :: other [b]

class Outer [x]
    fun make []
        inner = new Counter [@x + 1]
        return "{inner :: twice []} {@x}"
    end
end
print (new Outer [10]) :: make []

fun outside []
    return this
end
begin
    outside []
rescue e
    print e
end
# expect: 3 twice is 6
# expect: 5 twice is 10 / 3
# expect: 22 10
# expect: 'this' can only be used inside a class
