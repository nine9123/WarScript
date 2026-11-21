# T3.8 (PROP-28): '+' of two known types it does not take (no text among them).
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
x = {1} + 2   # expect-compile-error: Cannot apply '+' to array and number
