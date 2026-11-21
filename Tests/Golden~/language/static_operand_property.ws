# T3.8 (PROP-28): '::' on a value of a known type (none is an instance).
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
class P [x]
end
y = 5 :: x   # expect-compile-error: Cannot access property 'x' on non-class value
