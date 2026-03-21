# test_deep_algo_misc.ws — Hanoi + Pascal's triangle

fun hanoi_moves [n]
    if n <= 0
        return 0
    end
    if n == 1
        return 1
    end
    return 2 * hanoi_moves [n - 1] + 1
end
assert hanoi_moves [1] == 1
assert hanoi_moves [2] == 3
assert hanoi_moves [3] == 7
assert hanoi_moves [4] == 15
assert hanoi_moves [10] == 1023

fun pascal_row [n]
    row = {1}
    loop i in 1..n + 1
        prev = row
        row = {1}
        loop j in 1..i + 1
            if j == i
                row << 1
            else
                row << prev{j - 1} + prev{j}
            end
        end
    end
    return row
end
assert pascal_row [0] == {1}
assert pascal_row [1] == {1, 1}
assert pascal_row [4] == {1, 4, 6, 4, 1}
assert pascal_row [6] == {1, 6, 15, 20, 15, 6, 1}
