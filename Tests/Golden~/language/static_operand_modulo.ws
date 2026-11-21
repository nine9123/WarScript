# T3.8 (PROP-28): '%' takes numbers.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
x = 5 % null   # expect-compile-error: Cannot apply '%' to number and null
