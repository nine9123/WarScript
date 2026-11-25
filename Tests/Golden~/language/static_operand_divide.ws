# T3.8 (PROP-28): '/' and '%' take numbers; the result of '-' is known to be a number.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
n = 4
x = (n - 1) / "2"   # expect-compile-error: Cannot apply '/' to number and text
