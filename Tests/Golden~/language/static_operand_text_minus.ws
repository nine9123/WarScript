# T3.8 (PROP-28): text subtraction with a literal or interpolated operand is a compile error.
# The dynamic case (an operand from a variable or a call) compiles and
# raises the TypeError at runtime: static_operand_dynamic.ws.
name = "Bob"
x = "Sir {name}" - "Sir "   # expect-compile-error: Cannot apply '-' to text and text
