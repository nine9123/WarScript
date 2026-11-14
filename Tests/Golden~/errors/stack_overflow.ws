# Unbounded (non-tail) recursion is a LimitExceeded error, raised at the call that
# does not fit (plan T2.5).
fun down [n]
    return 1 + down [n + 1]   # expect-error: LimitExceeded: Stack overflow
end
down [0]
