# ──────────────────────────────────────────────────────
# test_deep_arrays.ws
# Deep array tests: matrix operations, array algorithms,
# arrays of classes with methods, array as class property
# with indexed access via ::, nested array patterns
# ──────────────────────────────────────────────────────

# ── 1. Matrix creation and access ──
fun make_matrix [rows, cols, default_val]
    m = {}
    loop r in 0..rows
        row = {}
        loop c in 0..cols
            row << default_val
        end
        m << row
    end
    return m
end

m = make_matrix [3, 4, 0]
row0 = m{0}
assert row0 == {0, 0, 0, 0}
row1 = m{1}
assert row1 == {0, 0, 0, 0}

# ── 2. Matrix set and get via intermediate vars ──
fun mat_set [m, r, c, val]
    row = m{r}
    row{c} = val
end
fun mat_get [m, r, c]
    row = m{r}
    return row{c}
end

mat_set [m, 0, 0, 1]
mat_set [m, 1, 1, 5]
mat_set [m, 2, 3, 9]
assert mat_get [m, 0, 0] == 1
assert mat_get [m, 1, 1] == 5
assert mat_get [m, 2, 3] == 9
assert mat_get [m, 0, 1] == 0

# ── 3. Identity matrix ──
fun make_identity [n]
    m = {}
    loop i in 0..n
        row = {}
        loop j in 0..n
            if i == j
                row << 1
            else
                row << 0
            end
        end
        m << row
    end
    return m
end

id3 = make_identity [3]
assert id3{0} == {1, 0, 0}
assert id3{1} == {0, 1, 0}
assert id3{2} == {0, 0, 1}

# ── 4. Selection sort (in-place) ──
fun selection_sort [arr, n]
    loop i in 0..n - 1
        min_idx = i
        loop j in i + 1..n
            if arr{j} < arr{min_idx}
                min_idx = j
            end
        end
        if min_idx != i
            temp = arr{i}
            arr{i} = arr{min_idx}
            arr{min_idx} = temp
        end
    end
end

data = {64, 25, 12, 22, 11}
selection_sort [data, 5]
assert data == {11, 12, 22, 25, 64}

data2 = {5, 4, 3, 2, 1}
selection_sort [data2, 5]
assert data2 == {1, 2, 3, 4, 5}

data3 = {1, 2, 3, 4, 5}
selection_sort [data3, 5]
assert data3 == {1, 2, 3, 4, 5}

# ── 5. Array filtering ──
fun filter_greater [arr, threshold]
    result = {}
    loop item in arr
        if item > threshold
            result << item
        end
    end
    return result
end

assert filter_greater [{1, 5, 3, 8, 2, 7}, 4] == {5, 8, 7}
assert filter_greater [{1, 2, 3}, 10] == {}
assert filter_greater [{}, 0] == {}

# ── 6. Array map (double each) ──
fun map_double [arr]
    result = {}
    loop item in arr
        result << item * 2
    end
    return result
end
assert map_double [{1, 2, 3, 4}] == {2, 4, 6, 8}
assert map_double [{}] == {}

# ── 7. Array reduce (sum) ──
fun reduce_sum [arr]
    total = 0
    loop item in arr
        total += item
    end
    return total
end
assert reduce_sum [{1, 2, 3, 4, 5}] == 15
assert reduce_sum [{}] == 0

# ── 8. Zip two arrays ──
fun zip_arrays [a, b, len]
    result = {}
    loop i in 0..len
        pair = {a{i}, b{i}}
        result << pair
    end
    return result
end
zipped = zip_arrays [{1, 2, 3}, {"a", "b", "c"}, 3]
z0 = zipped{0}
z1 = zipped{1}
z2 = zipped{2}
assert z0 == {1, "a"}
assert z1 == {2, "b"}
assert z2 == {3, "c"}

# ── 9. Flatten nested arrays ──
fun flatten [nested, len]
    result = {}
    loop i in 0..len
        inner = nested{i}
        loop item in inner
            result << item
        end
    end
    return result
end
assert flatten [{{1, 2}, {3, 4}, {5}}, 3] == {1, 2, 3, 4, 5}
assert flatten [{{}, {1}, {}}, 3] == {1}

# ── 10. Array of class instances with sorting ──
class Score [name, value]
end

scores = {}
scores << new Score ["Alice", 85]
scores << new Score ["Bob", 92]
scores << new Score ["Charlie", 78]
scores << new Score ["Dave", 95]
scores << new Score ["Eve", 88]

# Sort by value (selection sort on class property)
n = 5
loop i in 0..n - 1
    max_idx = i
    loop j in i + 1..n
        if scores{j} :: value > scores{max_idx} :: value
            max_idx = j
        end
    end
    if max_idx != i
        temp = scores{i}
        scores{i} = scores{max_idx}
        scores{max_idx} = temp
    end
end

assert scores{0} :: name == "Dave"
assert scores{0} :: value == 95
assert scores{1} :: name == "Bob"
assert scores{4} :: name == "Charlie"

# ── 11. Stack via array ──
stack = {}
stack << 10
stack << 20
stack << 30

# Pop manually
popped = {}
loop i in 0..3
    last_idx = 3 - i - 1
    val = stack{last_idx}
    popped << val
end
assert popped == {30, 20, 10}

# ── 12. Unique elements ──
fun unique [arr]
    result = {}
    loop item in arr
        found = false
        loop existing in result
            if existing == item
                found = true
                break
            end
        end
        if !found
            result << item
        end
    end
    return result
end
assert unique [{1, 2, 3, 2, 1, 4, 3}] == {1, 2, 3, 4}
assert unique [{5, 5, 5}] == {5}
assert unique [{}] == {}
assert unique [{1, 2, 3}] == {1, 2, 3}

# ── 13. Array equality deep check ──
assert {1, 2, 3} == {1, 2, 3}
assert {1, 2, 3} != {3, 2, 1}
assert {} == {}
assert {{1, 2}, {3, 4}} == {{1, 2}, {3, 4}}
assert {{1, 2}, {3, 4}} != {{1, 2}, {4, 3}}
