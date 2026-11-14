# Arithmetic on values it does not apply to is a TypeError (plan T2.5).
x = true * 2   # expect-error: TypeError: Unable to multiply non-numeric values
