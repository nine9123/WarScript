# T3.8 (PROP-28): unary '-' takes a number.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
x = -"x"   # expect-compile-error: Cannot apply '-' to text
