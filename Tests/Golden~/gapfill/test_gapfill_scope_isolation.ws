# ──────────────────────────────────────────────────────
# test_gapfill_scope_isolation.ws
# Gap: no test ever used the same variable name for both
# an outer variable and a loop counter. Tests also never
# checked that iterable loop counters are isolated, or
# that nested loops with same counter name work correctly.
# ──────────────────────────────────────────────────────

# ════════════════════════════════════════════════
#  For-loop counter isolation
# ════════════════════════════════════════════════

# ── Outer variable should survive loop with same name ──
i = 100
loop i in 0..5
end
assert i == 100

# ── Outer variable used AFTER loop ──
x = "original"
loop x in 0..3
end
assert x == "original"

# ── Outer numeric should not be clobbered ──
counter = 999
sum = 0
loop counter in 0..10
    sum += counter
end
assert counter == 999
assert sum == 45

# ── Variable with same name in different scopes ──
val = 50
if true
    loop val in 0..3
    end
end
assert val == 50

# ════════════════════════════════════════════════
#  Nested for-loops with same counter
# ════════════════════════════════════════════════

# ── Inner loop should not affect outer loop ──
outer_count = 0
loop i in 0..4
    loop i in 0..3
    end
    outer_count += 1
end
assert outer_count == 4

# ── Triple nesting same counter ──
depth_check = 0
loop i in 0..2
    loop i in 0..2
        loop i in 0..2
            depth_check += 1
        end
    end
end
assert depth_check == 8

# ════════════════════════════════════════════════
#  Iterable loop counter isolation
# ════════════════════════════════════════════════

# ── Iterable loop should not clobber outer variable ──
item = "untouched"
arr = {10, 20, 30}
total = 0
loop item in arr
    total += item
end
assert item == "untouched"
assert total == 60

# ── Nested iterable loops same counter ──
grid = {{1, 2}, {3, 4}, {5, 6}}
flat_sum = 0
loop row in grid
    loop row in row
        flat_sum += row
    end
end
assert flat_sum == 21

# ════════════════════════════════════════════════
#  For-loop with step, same counter
# ════════════════════════════════════════════════

n = 42
collected = {}
loop n in 0..10 by 2
    collected << n
end
assert n == 42
assert collected{0} == 0
assert collected{1} == 2
assert collected{2} == 4

# ════════════════════════════════════════════════
#  Loop counter inside function
# ════════════════════════════════════════════════

fun compute_sum[limit]
    x = 999
    result = 0
    loop x in 0..limit
        result += x
    end
    # x should still be 999 after loop
    assert x == 999
    return result
end
assert compute_sum[5] == 10
assert compute_sum[10] == 45

# ── Function with parameter name reused in different loops ──
fun multi_loop_test[n]
    sum1 = 0
    loop n in 0..5
        sum1 += n
    end
    # n should still be the parameter
    sum2 = 0
    loop n in 0..3
        sum2 += n
    end
    # n still the original parameter
    return n * 100 + sum1 + sum2
end
# n=7: sum1=10, sum2=3, result=7*100+10+3=713
assert multi_loop_test[7] == 713
assert multi_loop_test[0] == 13

# ════════════════════════════════════════════════
#  While-loop variable isolation (control case)
#  While loops DON'T have a counter variable so this
#  tests the intended behavior: Set() CAN mutate outer.
# ════════════════════════════════════════════════

w = 0
loop w < 5
    w = w + 1
end
# While loops intentionally modify 'w' in outer scope
assert w == 5

# ════════════════════════════════════════════════
#  Class method with loop counter same as property
# ════════════════════════════════════════════════

class Counter[n]
    fun count_up
        result = 0
        loop n in 0..this :: n
            result += n
        end
        return result
    end
end

c = new Counter[5]
assert c :: count_up[] == 10
# Property should be unchanged
assert c :: n == 5
