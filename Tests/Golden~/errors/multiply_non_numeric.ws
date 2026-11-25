# Arithmetic on values it does not apply to is a TypeError (plan T2.5).
t = true       # a variable: with a literal operand it is a compile error (PROP-28)
x = t * 2      # expect-error: TypeError: Cannot apply '*' to logical and number
