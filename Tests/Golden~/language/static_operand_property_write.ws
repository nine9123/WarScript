# T3.8 (PROP-28): a property write on a value of a known type.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
class P [x]
end
({1}) :: x = 2   # expect-compile-error: Cannot set property 'x' on non-class value
