# test_deep_rec_arrays.ws — Recursive array operations

fun arr_sum_rec [arr, i, n]
    if i >= n
        return 0
    end
    return arr{i} + arr_sum_rec [arr, i + 1, n]
end
data = {10, 20, 30, 40, 50}
assert arr_sum_rec [data, 0, 5] == 150
assert arr_sum_rec [data, 2, 5] == 120
assert arr_sum_rec [data, 0, 0] == 0

fun arr_max [arr, i, n]
    if i == n - 1
        return arr{i}
    end
    if arr{i} > arr_max [arr, i + 1, n]
        return arr{i}
    end
    return arr_max [arr, i + 1, n]
end
assert arr_max [{3, 7, 2, 9, 1}, 0, 5] == 9
assert arr_max [{5}, 0, 1] == 5
assert arr_max [{1, 2, 3, 4, 5}, 0, 5] == 5

fun count_val [arr, val, i, n]
    if i >= n
        return 0
    end
    if arr{i} == val
        return 1 + count_val [arr, val, i + 1, n]
    end
    return count_val [arr, val, i + 1, n]
end
assert count_val [{1, 2, 3, 2, 1, 2}, 2, 0, 6] == 3
assert count_val [{1, 2, 3}, 4, 0, 3] == 0
assert count_val [{5, 5, 5}, 5, 0, 3] == 3
