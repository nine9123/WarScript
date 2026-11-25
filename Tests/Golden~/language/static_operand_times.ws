# T3.8 (PROP-28): '*' of text and text.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
x = "a" * "b"   # expect-compile-error: Cannot apply '*' to text and text
