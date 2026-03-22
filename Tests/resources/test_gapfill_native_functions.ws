# ──────────────────────────────────────────────────────
# test_gapfill_native_functions.ws
# Gap: UtilityLibrary (is_null) was never tested.
# Also: verify every array/math function edge case.
# ──────────────────────────────────────────────────────

# ════════════════════════════════════════════════
#  is_null — was completely untested
# ════════════════════════════════════════════════

# ── Literal null ──
assert is_null[null] == true

# ── Default (uninitialized) variable ──
assert is_null[undefined_xyz] == true

# ── Non-null primitives ──
assert is_null[0] == false
assert is_null[1] == false
assert is_null[-1] == false
assert is_null[3.14] == false
assert is_null[""] == false
assert is_null["hello"] == false
assert is_null[true] == false
assert is_null[false] == false

# ── Array (not null even if empty) ──
assert is_null[{}] == false
assert is_null[{1,2,3}] == false

# ── Variable transitions ──
x = 42
assert is_null[x] == false
x = null
assert is_null[x] == true
x = "back"
assert is_null[x] == false

# ── Null inside array ──
arr = {null, 1, null, 2}
assert is_null[arr{0}] == true
assert is_null[arr{1}] == false
assert is_null[arr{2}] == true
assert is_null[arr{3}] == false

# ── Null from function return ──
fun returns_null
    return null
end
assert is_null[returns_null[]] == true

fun returns_value
    return 42
end
assert is_null[returns_value[]] == false

# ── Null class property ──
class Box[value]
end
b = new Box[null]
assert is_null[b :: value] == true
b2 = new Box[10]
assert is_null[b2 :: value] == false

# ════════════════════════════════════════════════
#  Array library edge cases
# ════════════════════════════════════════════════

# ── Array_length on empty ──
assert Array_length[{}] == 0

# ── Array_remove_at boundary ──
arr2 = {10, 20, 30}
removed = Array_remove_at[arr2, 0]
assert removed == 10
assert Array_length[arr2] == 2
assert arr2{0} == 20

# ── Array_remove_at last element ──
arr3 = {1, 2, 3}
removed2 = Array_remove_at[arr3, 2]
assert removed2 == 3
assert Array_length[arr3] == 2

# ── Array_remove_at out of bounds returns null ──
arr4 = {1, 2}
result = Array_remove_at[arr4, 5]
assert is_null[result]
assert Array_length[arr4] == 2

# ── Array_contains with different types ──
mixed = {1, "two", true, null}
assert Array_contains[mixed, 1] == true
assert Array_contains[mixed, "two"] == true
assert Array_contains[mixed, true] == true
assert Array_contains[mixed, 99] == false

# ── Array_index_of not found ──
assert Array_index_of[{1,2,3}, 99] == -1
assert Array_index_of[{}, 1] == -1

# ── Array_pop on single element ──
single = {42}
popped = Array_pop[single]
assert popped == 42
assert Array_length[single] == 0

# ── Array_pop on empty ──
empty = {}
popped2 = Array_pop[empty]
assert is_null[popped2]

# ── Array_insert at boundaries ──
ins = {1, 3}
Array_insert[ins, 1, 2]
assert ins{0} == 1
assert ins{1} == 2
assert ins{2} == 3

# ── Array_insert at 0 ──
Array_insert[ins, 0, 0]
assert ins{0} == 0
assert Array_length[ins] == 4

# ── Array_copy is independent ──
orig = {1, 2, 3}
copy = Array_copy[orig]
copy{0} = 99
assert orig{0} == 1
assert copy{0} == 99

# ── Array_clear ──
to_clear = {1, 2, 3, 4, 5}
Array_clear[to_clear]
assert Array_length[to_clear] == 0

# ════════════════════════════════════════════════
#  Math library edge cases
# ════════════════════════════════════════════════

# ── pow edge cases ──
assert pow[2, 0] == 1
assert pow[0, 5] == 0
assert pow[1, 1000] == 1
assert pow[2, 10] == 1024

# ── sqrt ──
assert sqrt[0] == 0
assert sqrt[1] == 1
assert sqrt[4] == 2
assert sqrt[9] == 3

# ── floor/ceil/round ──
assert floor[3.7] == 3
assert floor[3.0] == 3
assert floor[-1.5] == -2
assert ceil[3.1] == 4
assert ceil[3.0] == 3
assert ceil[-1.5] == -1
assert round[3.4] == 3
assert round[3.5] == 4

# ── abs ──
assert abs[-5] == 5
assert abs[5] == 5
assert abs[0] == 0

# ── min/max ──
assert min[3, 7] == 3
assert min[-1, 1] == -1
assert max[3, 7] == 7
assert max[-1, 1] == 1

# ── clamp ──
assert clamp[5, 0, 10] == 5
assert clamp[-5, 0, 10] == 0
assert clamp[15, 0, 10] == 10
assert clamp[0, 0, 10] == 0
assert clamp[10, 0, 10] == 10

# ── sign ──
assert sign[42] == 1
assert sign[-42] == -1
assert sign[0] == 0

# ── lerp ──
assert lerp[0, 10, 0] == 0
assert lerp[0, 10, 1] == 10
assert lerp[0, 10, 0.5] == 5
