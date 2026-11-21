# T3.8 (PROP-29): 'obj :: m [...]' naming no field or method of any class is
# a compile error (at runtime it is a NameError: errors/method_not_found.ws).
class Unit [hp]
    fun heal [n]
        @hp = @hp + n
    end
end
u = new Unit [10]
u :: haal [5]   # expect-compile-error: 'haal' is not a field or method of any class
