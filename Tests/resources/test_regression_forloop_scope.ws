# ──────────────────────────────────────────────────────
# test_regression_forloop_scope.ws
# Bug: ForLoopStatement.Init() uses Set() instead of
#      SetLocal(), so if a variable with the same name
#      exists in an outer scope, the loop mutates it.
#
# After "loop i in 0..5 end", an outer "i" should be
# unchanged.
# ──────────────────────────────────────────────────────

# ── Outer variable should survive a loop using same name ──
i = 100
loop i in 0..5
end
assert i == 100

# ── Nested loops with same counter name ──
outer = 0
loop i in 0..3
    loop i in 0..2
    end
    outer += 1
end
assert outer == 3

# ── Variable defined before loop should keep its value ──
counter = 999
loop counter in 0..10
end
assert counter == 999

# ── Variable with same name in a function should be safe ──
fun check_scope
    x = 42
    loop x in 0..5
    end
    return x
end
result = check_scope[]
assert result == 42
