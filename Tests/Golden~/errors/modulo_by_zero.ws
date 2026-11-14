# Modulo by zero is a DivisionByZero error too (plan T2.5).
n = 7
r = n % 0   # expect-error: DivisionByZero: Modulo by zero
