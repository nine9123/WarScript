# T3.8 (PROP-28): a const has the type of its declared value, even when it is
# read above the declaration or in another file of the module set (a read
# before the declaration runs is a NameError, and no other write compiles).
# A parameter named like the constant is not the constant:
# static_operand_const_shadowed.ws.
fun greet()
    return TITLE - "Sir "   # expect-compile-error: Cannot apply '-' to text and text
end
const TITLE = "Sir " + 1
print greet()
