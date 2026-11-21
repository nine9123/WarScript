# Calling a method on a value that is not an instance is a TypeError
# (plan T2.5).
# A class has such a method, so only the VM can tell (T3.8, PROP-29).
class Unit
    fun hit [n]
    end
end
x = 5
x :: hit [1]   # expect-error: TypeError: Cannot call method 'hit' on non-class value
