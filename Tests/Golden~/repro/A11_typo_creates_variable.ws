# Plan A.11 (SEM-2, PROP-26): a typo on the left of an assignment makes a
# new local, which the compiler warns about (T3.8).
health = 100
fun damage [amt]
    helth = health - amt      # expect-warning: local 'helth' is assigned but never read
    return health
end
print damage [30]             # expect: 100
