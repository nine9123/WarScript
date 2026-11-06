# known-failing: SEM-2
# Plan A.11 (PROP-26): needs the compile-time warning from T3.8.
health = 100
fun damage [amt]
    helth = health - amt      # expect-warning: local 'helth' is assigned but never read
    return health
end
print damage [30]             # expect: 100
