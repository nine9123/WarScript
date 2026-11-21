# ──────────────────────────────────────────────────────
# test_arrays.ws
# Covers: creation, indexing, assignment, append (<<),
#         concatenation (+), equality, nested arrays,
#         arrays of classes, iteration, modification
# ──────────────────────────────────────────────────────

# ── Array creation ──
arr = {1, 2, 3}
assert arr == {1, 2, 3}

# ── Empty array ──
empty = {}
assert empty == {}

# ── Single element ──
single = {42}
assert single == {42}

# ── Mixed types ──
mixed = {1, "two", true, null}
assert mixed{0} == 1
assert mixed{1} == "two"
assert mixed{2} == true
assert mixed{3} == null

# ── Index access ──
arr = {10, 20, 30, 40, 50}
assert arr{0} == 10
assert arr{1} == 20
assert arr{2} == 30
assert arr{3} == 40
assert arr{4} == 50

# ── Index assignment ──
arr = {1, 2, 3}
arr{0} = 10
arr{1} = 20
arr{2} = 30
assert arr == {10, 20, 30}

# ── Append operator << ──
arr = {}
arr << 1
arr << 2
arr << 3
assert arr == {1, 2, 3}

# ── Append different types ──
arr = {}
arr << 42
arr << "hello"
arr << true
arr << null
assert arr{0} == 42
assert arr{1} == "hello"
assert arr{2} == true
assert arr{3} == null

# ── Array concatenation ──
a = {1, 2, 3}
b = {4, 5, 6}
c = a + b
assert c == {1, 2, 3, 4, 5, 6}

# ── Concatenation doesn't mutate originals ──
assert a == {1, 2, 3}
assert b == {4, 5, 6}

# ── Concatenation with empty ──
assert {1, 2} + {} == {1, 2}
assert {} + {3, 4} == {3, 4}
assert {} + {} == {}

# ── Array equality ──
assert {1, 2, 3} == {1, 2, 3}
assert {1, 2, 3} != {3, 2, 1}
assert {1, 2} != {1, 2, 3}
assert {} == {}
assert {} != {1}
assert {"a", "b"} == {"a", "b"}

# ── Nested arrays (use intermediate variable for chained access) ──
matrix = {{1, 2}, {3, 4}, {5, 6}}
assert matrix{0} == {1, 2}
assert matrix{1} == {3, 4}
assert matrix{2} == {5, 6}
row0 = matrix{0}
row1 = matrix{1}
row2 = matrix{2}
assert row0{0} == 1
assert row0{1} == 2
assert row1{0} == 3
assert row2{1} == 6

# ── Array of strings ──
names = {"Alice", "Bob", "Charlie"}
assert names{0} == "Alice"
assert names{2} == "Charlie"

# ── Building arrays in loops ──
squares = {}
loop i in 0..6
    squares << i * i
end
assert squares == {0, 1, 4, 9, 16, 25}

# ── Iterating over array ──
arr = {10, 20, 30}
total = 0
loop item in arr
    total += item
end
assert total == 60

# ── Array in function ──
fun reverse_arr [arr, n]
    result = {}
    loop i in 0..n
        result << arr{n - 1 - i}
    end
    return result
end
assert reverse_arr [{1, 2, 3, 4, 5}, 5] == {5, 4, 3, 2, 1}

# ── Array passed by reference (mutation) ──
fun add_element [arr, val]
    arr << val
end
my_arr = {1, 2}
add_element [my_arr, 3]
assert my_arr == {1, 2, 3}

# ── Array of booleans ──
flags = {true, false, true, false}
assert flags{0} == true
assert flags{1} == false

# ── Array with computed indices ──
arr = {0, 10, 20, 30, 40}
idx = 2
assert arr{idx} == 20
assert arr{idx + 1} == 30
assert arr{idx * 2} == 40

# ── Array modification in place ──
arr = {1, 2, 3, 4, 5}
loop i in 0..5
    arr{i} = arr{i} * 2
end
assert arr == {2, 4, 6, 8, 10}

# ── Array with class instances ──
class Coord [x, y]
end
coords = {}
coords << new Coord [0, 0]
coords << new Coord [1, 2]
coords << new Coord [3, 4]
assert coords{0} :: x == 0
assert coords{1} :: x == 1
assert coords{1} :: y == 2
assert coords{2} :: x == 3

# ── Array append in loop with condition ──
evens = {}
odds = {}
loop i in 0..10
    if i % 2 == 0
        evens << i
    else
        odds << i
    end
end
assert evens == {0, 2, 4, 6, 8}
assert odds == {1, 3, 5, 7, 9}

# ── Compound assignment on array element ──
arr = {100, 200, 300}
arr{0} += 50
arr{1} -= 100
arr{2} *= 2
assert arr == {150, 100, 600}

# ── Nested array building ──
grid = {}
loop r in 0..3
    row = {}
    loop c in 0..3
        row << r * 10 + c
    end
    grid << row
end
g0 = grid{0}
g1 = grid{1}
g2 = grid{2}
assert g0{0} == 0
assert g1{2} == 12
assert g2{2} == 22
