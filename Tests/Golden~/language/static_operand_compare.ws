# T3.8 (PROP-28): ordering takes two numbers or two texts; a comparison is logical.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
n = 1
x = n < 2 < 3   # expect-compile-error: Cannot apply '<' to logical and number
