# ──────────────────────────────────────────────────────
# test_array_library.ws
# Covers: Array_length, Array_contains, Array_index_of,
#         Array_remove, Array_remove_at, Array_clear,
#         Array_pop, Array_insert, Array_copy
# Requires ArrayLibrary to be registered
# ──────────────────────────────────────────────────────

# ── Array_length ──
assert Array_length [{}] == 0
assert Array_length [{1}] == 1
assert Array_length [{1, 2, 3}] == 3
assert Array_length [{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}] == 10

arr = {}
loop i in 0..5
    arr << i
end
assert Array_length [arr] == 5

# ── Array_contains ──
arr = {10, 20, 30, 40, 50}
assert Array_contains [arr, 10]
assert Array_contains [arr, 30]
assert Array_contains [arr, 50]
assert !Array_contains [arr, 15]
assert !Array_contains [arr, 0]
assert !Array_contains [{}, 1]

# ── Array_contains with strings ──
names = {"Alice", "Bob", "Charlie"}
assert Array_contains [names, "Alice"]
assert Array_contains [names, "Charlie"]
assert !Array_contains [names, "Dave"]
assert !Array_contains [names, "alice"]

# ── Array_index_of ──
arr = {10, 20, 30, 40, 50}
assert Array_index_of [arr, 10] == 0
assert Array_index_of [arr, 30] == 2
assert Array_index_of [arr, 50] == 4
assert Array_index_of [arr, 99] == -1
assert Array_index_of [{}, 1] == -1

# ── Array_index_of with duplicates (finds first) ──
arr = {1, 2, 3, 2, 1}
assert Array_index_of [arr, 2] == 1
assert Array_index_of [arr, 1] == 0

# ── Array_remove (by value) ──
arr = {1, 2, 3, 4, 5}
result = Array_remove [arr, 3]
assert result == true
assert arr == {1, 2, 4, 5}

result = Array_remove [arr, 99]
assert result == false
assert arr == {1, 2, 4, 5}

# ── Array_remove first occurrence only ──
arr = {1, 2, 3, 2, 1}
Array_remove [arr, 2]
assert arr == {1, 3, 2, 1}

# ── Array_remove_at ──
arr = {10, 20, 30, 40, 50}
removed = Array_remove_at [arr, 2]
assert removed == 30
assert arr == {10, 20, 40, 50}

removed2 = Array_remove_at [arr, 0]
assert removed2 == 10
assert arr == {20, 40, 50}

# ── Array_remove_at out of bounds ──
arr = {1, 2, 3}
result = Array_remove_at [arr, 10]
assert result == null
assert arr == {1, 2, 3}

result2 = Array_remove_at [arr, -1]
assert result2 == null

# ── Array_pop ──
arr = {1, 2, 3, 4, 5}
assert Array_pop [arr] == 5
assert arr == {1, 2, 3, 4}
assert Array_pop [arr] == 4
assert arr == {1, 2, 3}
assert Array_pop [arr] == 3
assert Array_pop [arr] == 2
assert Array_pop [arr] == 1
assert arr == {}

# ── Array_pop on empty ──
empty = {}
assert Array_pop [empty] == null

# ── Array_insert ──
arr = {1, 2, 3}
Array_insert [arr, 1, 99]
assert arr == {1, 99, 2, 3}

Array_insert [arr, 0, 0]
assert arr == {0, 1, 99, 2, 3}

Array_insert [arr, 5, 100]
assert arr == {0, 1, 99, 2, 3, 100}

# ── Array_clear ──
arr = {1, 2, 3, 4, 5}
Array_clear [arr]
assert arr == {}
assert Array_length [arr] == 0

# ── Array_clear on empty ──
empty = {}
Array_clear [empty]
assert empty == {}

# ── Array_copy ──
original = {1, 2, 3}
copy = Array_copy [original]
assert copy == {1, 2, 3}

# ── Copy is independent ──
copy << 4
assert copy == {1, 2, 3, 4}
assert original == {1, 2, 3}

original{0} = 99
assert original{0} == 99
assert copy{0} == 1

# ── Complex: stack with array library ──
stack = {}
stack << 10
stack << 20
stack << 30
assert Array_length [stack] == 3
assert Array_pop [stack] == 30
assert Array_pop [stack] == 20
assert Array_pop [stack] == 10
assert Array_length [stack] == 0

# ── Complex: removing all matching elements ──
arr = {1, 2, 3, 2, 4, 2, 5}
loop Array_contains [arr, 2]
    Array_remove [arr, 2]
end
assert arr == {1, 3, 4, 5}
assert !Array_contains [arr, 2]

# ── Insert at various positions ──
arr = {}
Array_insert [arr, 0, "a"]
Array_insert [arr, 1, "c"]
Array_insert [arr, 1, "b"]
assert arr == {"a", "b", "c"}
