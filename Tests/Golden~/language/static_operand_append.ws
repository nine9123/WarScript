# T3.8 (PROP-28): '<<' needs an array.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
x = 5 << 3   # expect-compile-error: Cannot apply '<<' to number and number
