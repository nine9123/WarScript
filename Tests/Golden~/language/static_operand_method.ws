# T3.8 (PROP-28): a method call on a value of a known type.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
class P(x)
    fun get()
        return @x
    end
end
y = "p".get()   # expect-compile-error: Cannot call method 'get' on non-class value
