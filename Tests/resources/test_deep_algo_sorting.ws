# Sorting algorithms: bubble, selection, insertion
# (each in its own function, tested independently)

# ── 1. Bubble sort ──
fun bubble_sort [arr, n]
    loop i in 0..n - 1
        loop j in 0..n - i - 1
            if arr{j} > arr{j + 1}
                temp = arr{j}
                arr{j} = arr{j + 1}
                arr{j + 1} = temp
            end
        end
    end
end

d1 = {5, 3, 8, 1, 9}
bubble_sort [d1, 5]
assert d1 == {1, 3, 5, 8, 9}

d2 = {1, 2, 3, 4, 5}
bubble_sort [d2, 5]
assert d2 == {1, 2, 3, 4, 5}

d3 = {5, 4, 3, 2, 1}
bubble_sort [d3, 5]
assert d3 == {1, 2, 3, 4, 5}

d4 = {1}
bubble_sort [d4, 1]
assert d4 == {1}

# ── 2. Selection sort ──
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

d5 = {64, 25, 12, 22, 11}
selection_sort [d5, 5]
assert d5 == {11, 12, 22, 25, 64}

d6 = {3, 3, 3}
selection_sort [d6, 3]
assert d6 == {3, 3, 3}

# ── 3. Insertion sort ──
fun insertion_sort [arr, n]
    loop i in 1..n
        key = arr{i}
        j = i - 1
        loop j >= 0 and arr{j} > key
            arr{j + 1} = arr{j}
            j -= 1
        end
        arr{j + 1} = key
    end
end

d7 = {12, 11, 13, 5, 6}
insertion_sort [d7, 5]
assert d7 == {5, 6, 11, 12, 13}

d8 = {10, 9, 8, 7, 6, 5, 4, 3, 2, 1}
insertion_sort [d8, 10]
assert d8 == {1, 2, 3, 4, 5, 6, 7, 8, 9, 10}

# ── 4. Verify sort stability concept (equal elements keep order) ──
d9 = {3, 1, 4, 1, 5, 9, 2, 6}
insertion_sort [d9, 8]
assert d9 == {1, 1, 2, 3, 4, 5, 6, 9}

# ── 5. Is-sorted check ──
fun is_sorted [arr, n]
    loop i in 0..n - 1
        if arr{i} > arr{i + 1}
            return false
        end
    end
    return true
end

assert is_sorted [{1, 2, 3, 4, 5}, 5]
sorted_check = is_sorted [{5, 3, 1}, 3]
assert !sorted_check
assert is_sorted [{1}, 1]
assert is_sorted [{1, 1, 1}, 3]
